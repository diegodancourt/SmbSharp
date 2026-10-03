using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Extensions.Logging;
using SmbSharp.Infrastructure.Interfaces;

namespace SmbSharp.Infrastructure
{
    /// <summary>
    /// Concrete implementation of IProcessWrapper that executes real processes.
    /// </summary>
    [ExcludeFromCodeCoverage]
    internal class ProcessWrapper : IProcessWrapper
    {
        private readonly ILogger<ProcessWrapper>? _logger;

        public ProcessWrapper(ILogger<ProcessWrapper>? logger = null)
        {
            _logger = logger;
        }

        /// <inheritdoc/>
        public Task<ProcessResult> ExecuteAsync(string fileName, string arguments,
            IDictionary<string, string>? environmentVariables = null,
            CancellationToken cancellationToken = default) =>
            ExecuteCoreAsync(fileName, new[] { arguments }, environmentVariables, cancellationToken, arguments);

        /// <inheritdoc/>
        public Task<ProcessResult> ExecuteAsync(string fileName, IEnumerable<string> argumentList,
            IDictionary<string, string>? environmentVariables = null,
            CancellationToken cancellationToken = default)
        {
            var args = argumentList.ToArray();
            return ExecuteCoreAsync(fileName, args, environmentVariables, cancellationToken, null);
        }

        private async Task<ProcessResult> ExecuteCoreAsync(string fileName, IReadOnlyList<string> argumentList,
            IDictionary<string, string>? environmentVariables, CancellationToken cancellationToken,
            string? legacyArguments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_logger?.IsEnabled(LogLevel.Debug) == true)
            {
                _logger.LogDebug("Starting external process {fileName}", fileName);
                if (environmentVariables != null && environmentVariables.Count > 0)
                {
                    var envVarNames = string.Join(", ", environmentVariables.Keys);
                    _logger.LogDebug("Environment variables set: {environmentVariables}", envVarNames);
                }
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (legacyArguments != null)
                processStartInfo.Arguments = legacyArguments;
            else
                foreach (var arg in argumentList)
                    processStartInfo.ArgumentList.Add(arg);

            // Add environment variables if provided
            if (environmentVariables != null)
            {
                foreach (var kvp in environmentVariables)
                {
                    processStartInfo.Environment[kvp.Key] = kvp.Value;
                }
            }

            using var process = new Process();
            process.StartInfo = processStartInfo;
            process.Start();
            using var killOnCancellation = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            });

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);

#if NET6_0_OR_GREATER
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
#else
            await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
#endif
            var result = new ProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = outputTask.Result,
                StandardError = errorTask.Result
            };

            _logger?.LogDebug("External process exited with code {exitCode}", result.ExitCode);
            return result;
        }
    }
}
