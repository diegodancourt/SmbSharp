using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmbSharp.Business.Interfaces;

namespace SmbSharp.HealthChecks
{
    /// <summary>
    /// Health check implementation that verifies connectivity to an SMB share.
    /// </summary>
    public class SmbShareHealthCheck : IHealthCheck
    {
        private readonly IFileHandler _fileHandler;
        private readonly string _directoryPath;

        /// <summary>
        /// Initializes a new instance of the SmbShareHealthCheck class.
        /// </summary>
        /// <param name="fileHandler">The file handler to use for connectivity checks</param>
        /// <param name="directoryPath">The SMB directory path to check (e.g., "//server/share/path")</param>
        public SmbShareHealthCheck(IFileHandler fileHandler, string directoryPath)
        {
            _fileHandler = fileHandler ?? throw new ArgumentNullException(nameof(fileHandler));
            _directoryPath = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
        }

        /// <summary>
        /// Performs the health check by attempting to connect to the configured SMB share.
        /// </summary>
        /// <param name="context">The health check context</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>The health check result</returns>
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var canConnect = await _fileHandler.CanConnectAsync(_directoryPath, cancellationToken);

                if (canConnect)
                {
                    return HealthCheckResult.Healthy($"Successfully connected to SMB share: {_directoryPath}");
                }

                return HealthCheckResult.Unhealthy($"Unable to connect to SMB share: {_directoryPath}");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy(
                    $"Health check failed for SMB share {_directoryPath}: {ex.Message}",
                    ex);
            }
        }
    }
}
