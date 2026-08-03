namespace SmbSharp.Business.SmbClient.Session
{
    /// <summary>
    /// Thrown when a persistent smbclient session dies mid-operation (process crashed, or the remote
    /// end closed the connection). Callers (the session pool) can catch this to recreate the session
    /// and retry once.
    /// </summary>
    public class SmbSessionBrokenException : IOException
    {
        /// <summary>
        /// Creates a new <see cref="SmbSessionBrokenException"/> with the given message.
        /// </summary>
        public SmbSessionBrokenException(string message) : base(message)
        {
        }

        /// <summary>
        /// Creates a new <see cref="SmbSessionBrokenException"/> with the given message and inner exception.
        /// </summary>
        public SmbSessionBrokenException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
