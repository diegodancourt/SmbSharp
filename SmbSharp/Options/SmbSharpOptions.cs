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
    }
}