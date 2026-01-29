namespace SmbSharp.Infrastructure.Interfaces
{
    /// <summary>
    /// Abstraction for process execution to enable testing without actually running external processes.
    /// </summary>
    public interface IProcessWrapper
    {
        /// <summary>
        /// Executes a process with the given configuration.
        /// </summary>
        /// <param name="fileName">The application or document to start</param>
        /// <param name="arguments">Command-line arguments to pass to the application</param>
        /// <param name="environmentVariables">Environment variables to set for the process</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>A tuple containing the exit code, standard output, and standard error</returns>
        Task<ProcessResult> ExecuteAsync(string fileName, string arguments,
            IDictionary<string, string>? environmentVariables = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result of a process execution.
    /// </summary>
    public class ProcessResult
    {
        /// <summary>
        /// The exit code of the process.
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// The standard output from the process.
        /// </summary>
        public string StandardOutput { get; set; } = string.Empty;

        /// <summary>
        /// The standard error from the process.
        /// </summary>
        public string StandardError { get; set; } = string.Empty;
    }
}
