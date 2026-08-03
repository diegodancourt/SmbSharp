namespace SmbSharp.Options
{
    /// <summary>
    /// Configuration options for SmbSharp.
    /// </summary>
    public class SmbSharpOptions
    {
        /// <summary>
        /// Gets or sets whether to use Kerberos authentication. Default is true.
        /// </summary>
        public bool UseKerberos { get; set; } = true;

        /// <summary>
        /// Gets or sets the username for SMB authentication (required when UseKerberos is false).
        /// </summary>
        public string? Username { get; set; }

        /// <summary>
        /// Gets or sets the password for SMB authentication (required when UseKerberos is false).
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// Gets or sets the domain for SMB authentication (optional).
        /// </summary>
        public string? Domain { get; set; }

        /// <summary>
        /// Gets or sets whether to use smbclient via WSL on Windows. Default is false.
        /// When true, smbclient commands are executed through WSL instead of using native UNC paths.
        /// This option is only relevant on Windows; on Linux/macOS, smbclient is used directly.
        /// </summary>
        public bool UseWsl { get; set; }

        /// <summary>
        /// Gets or sets whether to keep a small pool of persistent, authenticated smbclient sessions
        /// open per share instead of spawning (and re-authenticating) a new process for every
        /// operation. Default is false for backward compatibility; strongly recommended when using
        /// Kerberos, since re-authenticating on every call is the dominant cost (including for
        /// frequent health checks).
        /// </summary>
        public bool UseSessionPool { get; set; }

        /// <summary>
        /// Gets or sets the number of persistent sessions kept per (server, share) when
        /// <see cref="UseSessionPool"/> is true. Default is 3. Concurrent operations against the
        /// same share are spread across these sessions instead of queuing behind a single session.
        /// </summary>
        public int SessionPoolSize { get; set; } = 3;

        /// <summary>
        /// Gets or sets how long a session may sit idle before it is disposed, when
        /// <see cref="UseSessionPool"/> is true. Default is 15 minutes.
        /// </summary>
        public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromMinutes(15);
    }
}