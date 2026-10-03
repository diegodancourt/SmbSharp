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
        /// Does not replay a command after dispatch because the outcome of an interrupted write can be uncertain.
        /// </summary>
        Task<string> ExecuteAsync(string server, string share, string command, string contextPath,
            CancellationToken cancellationToken = default);
    }
}
