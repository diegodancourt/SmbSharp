using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
        public async Task<ProcessResult> ExecuteAsync(string fileName, string arguments,
            IDictionary<string, string>? environmentVariables = null,
            CancellationToken cancellationToken = default)
        {
            // Log the command being executed (but not sensitive data like passwords)
            if (_logger?.IsEnabled(LogLevel.Debug) == true)
            {
                _logger.LogDebug("Executing process: {FileName} {Arguments}", fileName, arguments);
                _logger.LogDebug("Full command line would be: {FileName} {Arguments}", fileName, arguments);

                // Log each character in the arguments to debug encoding issues
                _logger.LogDebug("Arguments length: {Length}, bytes: {Bytes}",
                    arguments.Length,
                    string.Join(" ", arguments.Select((c, i) => $"{i}:{(int)c:X2}")));

                if (environmentVariables != null && environmentVariables.Count > 0)
                {
                    var envVarNames = string.Join(", ", environmentVariables.Keys);
                    _logger.LogDebug("Environment variables set: {EnvironmentVariables}", envVarNames);
                }
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

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

            // ReadToEndAsync with CancellationToken is only available in .NET 7+
#if NET7_0_OR_GREATER
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
#else
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
#endif

#if NETCOREAPP3_1
            // .NET Core 3.1 doesn't have WaitForExitAsync with cancellation token
            await Task.Run(() => process.WaitForExit(), cancellationToken);
#else
            await process.WaitForExitAsync(cancellationToken);
#endif

            var result = new ProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = output,
                StandardError = error
            };

            // Log the result
            if (_logger?.IsEnabled(LogLevel.Debug) == true)
            {
                _logger.LogDebug("Process exited with code: {ExitCode}", result.ExitCode);

                if (result.ExitCode != 0)
                {
                    _logger.LogDebug("Process stdout: {StandardOutput}", result.StandardOutput);
                    _logger.LogDebug("Process stderr: {StandardError}", result.StandardError);
                }
            }

            return result;
        }
    }
}
