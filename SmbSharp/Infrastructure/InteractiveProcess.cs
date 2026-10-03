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
        private const int ReadBufferSize = 4096;
        private const int TailWindowSize = 256;

        private readonly ILogger? _logger;
        private Process? _process;
        private readonly StringBuilder _pendingOutput = new();

        public InteractiveProcess(ILogger? logger = null)
        {
            _logger = logger;
        }

        public bool HasExited
        {
            get
            {
                if (_process == null)
                    return true;
                try
                {
                    return _process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        public void Start(string fileName, IEnumerable<string> argumentList,
            IDictionary<string, string>? environmentVariables = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
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
            _ = _process.StandardError.ReadToEndAsync();
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
            var buffer = new char[ReadBufferSize];

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
                var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
#else
                var read = await reader.ReadAsync(buffer, 0, buffer.Length);
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

                _pendingOutput.Append(buffer, 0, read);
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
