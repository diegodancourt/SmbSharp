using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using SmbSharp.Infrastructure.Interfaces;

namespace SmbSharp.Infrastructure
{
    /// <summary>
    /// Concrete implementation of IProcessWrapper that executes real processes.
    /// </summary>
    [ExcludeFromCodeCoverage]
    internal class ProcessWrapper : IProcessWrapper
    {
        /// <inheritdoc/>
        public async Task<ProcessResult> ExecuteAsync(string fileName, string arguments,
            IDictionary<string, string>? environmentVariables = null,
            CancellationToken cancellationToken = default)
        {
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

            return new ProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = output,
                StandardError = error
            };
        }
    }
}
