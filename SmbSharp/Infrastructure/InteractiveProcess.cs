using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SmbSharp.Infrastructure.Interfaces;

namespace SmbSharp.Infrastructure
{
    /// <summary>
    /// Concrete implementation of IInteractiveProcess backed by a real, long-lived child process
    /// with redirected standard input/output.
    /// </summary>
    [ExcludeFromCodeCoverage]
    internal class InteractiveProcess : IInteractiveProcess
    {
        // Only the tail of the accumulated output needs to be checked against the terminator regex,
        // so we cap how much text we re-scan on every read to avoid O(n^2) behavior on chatty output
        // (e.g. "get"/"put" progress lines).
        private const int TailWindowSize = 4096;

        private readonly ILogger? _logger;
        private Process? _process;
        private readonly StringBuilder _pendingOutput = new();

        public InteractiveProcess(ILogger? logger = null)
        {
            _logger = logger;
        }

        public bool HasExited => _process == null || _process.HasExited;

        public void Start(string fileName, IEnumerable<string> argumentList,
            IDictionary<string, string>? environmentVariables = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in argumentList)
            {
                startInfo.ArgumentList.Add(arg);
            }

            if (environmentVariables != null)
            {
                foreach (var kvp in environmentVariables)
                {
                    startInfo.Environment[kvp.Key] = kvp.Value;
                }
            }

            _process = new Process { StartInfo = startInfo };
            _process.Start();
        }

        public async Task WriteLineAsync(string line, CancellationToken cancellationToken = default)
        {
            if (_process == null)
                throw new InvalidOperationException("Process has not been started.");

            var writer = _process.StandardInput;
#if NET7_0_OR_GREATER
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
#else
            await writer.WriteLineAsync(line);
#endif
            await writer.FlushAsync();
        }

        public async Task<string> ReadUntilAsync(Regex terminator, CancellationToken cancellationToken = default)
        {
            if (_process == null)
                throw new InvalidOperationException("Process has not been started.");

            var reader = _process.StandardOutput;
            var buffer = new char[1];

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tail = _pendingOutput.Length > TailWindowSize
                    ? _pendingOutput.ToString(_pendingOutput.Length - TailWindowSize, TailWindowSize)
                    : _pendingOutput.ToString();

                var match = terminator.Match(tail);
                if (match.Success && match.Index + match.Length == tail.Length)
                {
                    // Consume everything up to (and including) the terminator from the pending buffer,
                    // returning the text before the terminator.
                    var fullText = _pendingOutput.ToString();
                    var terminatorStartInFull = fullText.Length - (tail.Length - match.Index);
                    var result = fullText.Substring(0, terminatorStartInFull);
                    _pendingOutput.Clear();
                    return result;
                }

#if NET7_0_OR_GREATER
                var read = await reader.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
#else
                var read = await reader.ReadAsync(buffer, 0, 1);
#endif
                if (read == 0)
                {
                    // EOF - the process closed its output stream (crashed, killed, or remote disconnect).
                    var remaining = _pendingOutput.ToString();
                    _pendingOutput.Clear();
                    throw new IOException(
                        $"Interactive process ended unexpectedly before the expected output was received. " +
                        $"Partial output: {remaining}");
                }

                _pendingOutput.Append(buffer[0]);
            }
        }

        public void Kill()
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Error killing interactive process.");
            }
        }

        public void Dispose()
        {
            Kill();
            _process?.Dispose();
        }
    }
}
