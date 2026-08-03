namespace SmbSharp.Business.SmbClient.Session
{
    /// <summary>
    /// Maintains a small pool of persistent, authenticated smbclient sessions per (server, share),
    /// so concurrent file operations against the same share don't serialize behind a single
    /// interactive process, while still avoiding a full re-authentication per call.
    /// </summary>
    internal interface ISmbClientSessionPool : IDisposable
    {
        /// <summary>
        /// Runs a single smbclient command against a pooled, persistent session for the given share.
        /// Transparently recreates and retries once if the selected session died mid-operation.
        /// </summary>
        Task<string> ExecuteAsync(string server, string share, string command, string contextPath,
            CancellationToken cancellationToken = default);
    }
}
