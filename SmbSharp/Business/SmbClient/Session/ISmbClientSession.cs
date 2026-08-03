namespace SmbSharp.Business.SmbClient.Session
{
    /// <summary>
    /// A single persistent, authenticated smbclient interactive process bound to one (server, share).
    /// Commands are serialized (a single interactive process can only run one command at a time);
    /// use <see cref="ISmbClientSessionPool"/> to run multiple commands concurrently against the same share.
    /// </summary>
    internal interface ISmbClientSession : IDisposable
    {
        /// <summary>
        /// True if the underlying process is still running and the session completed its initial handshake.
        /// </summary>
        bool IsAlive { get; }

        /// <summary>
        /// UTC timestamp of the last time this session successfully executed a command (or was created).
        /// </summary>
        DateTime LastUsedUtc { get; }

        /// <summary>
        /// Starts the smbclient process and waits for the initial connection/authentication to complete.
        /// </summary>
        /// <exception cref="UnauthorizedAccessException">Authentication failed.</exception>
        /// <exception cref="DirectoryNotFoundException">The share/network path could not be found.</exception>
        /// <exception cref="SmbSessionBrokenException">The process exited before completing the handshake.</exception>
        Task InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs a single smbclient command against the already-authenticated session and returns its output.
        /// </summary>
        /// <param name="command">The smbclient command (e.g. "ls", "get \"a\" \"b\"").</param>
        /// <param name="contextPath">Path used only for error messages.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="SmbSessionBrokenException">The session died mid-command; caller should recreate and may retry.</exception>
        Task<string> ExecuteAsync(string command, string contextPath, CancellationToken cancellationToken = default);
    }
}
