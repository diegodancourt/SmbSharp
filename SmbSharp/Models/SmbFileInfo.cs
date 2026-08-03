namespace SmbSharp.Models
{
    /// <summary>
    /// Extended metadata for a file or directory on an SMB share, as reported by smbclient's
    /// <c>allinfo</c> command (or the equivalent native APIs on Windows UNC paths).
    /// </summary>
    public class SmbFileInfo
    {
        /// <summary>
        /// The DOS 8.3 short name of the file, if reported (smbclient's <c>altname</c> field).
        /// Null on Windows native paths, where this information is not exposed by .NET.
        /// </summary>
        public string? AlternateName { get; set; }

        /// <summary>
        /// The file's creation time, if it could be determined.
        /// </summary>
        public DateTime? CreateTime { get; set; }

        /// <summary>
        /// The file's last access time, if it could be determined.
        /// </summary>
        public DateTime? AccessTime { get; set; }

        /// <summary>
        /// The file's last write (modification) time, if it could be determined.
        /// </summary>
        public DateTime? WriteTime { get; set; }

        /// <summary>
        /// The file's last metadata-change time, if it could be determined.
        /// On Windows native paths, .NET does not expose a distinct "change time",
        /// so this falls back to the write time.
        /// </summary>
        public DateTime? ChangeTime { get; set; }

        /// <summary>
        /// The raw file attribute string (e.g. smbclient's "A" for archive, "D" for directory, "H" for hidden),
        /// or .NET's <see cref="System.IO.FileAttributes"/> string representation on Windows native paths.
        /// </summary>
        public string? Attributes { get; set; }

        /// <summary>
        /// Alternate data streams reported for the file (e.g. "[:Zone.Identifier:$DATA], 26 bytes"),
        /// as returned verbatim by smbclient. Empty on Windows native paths, since .NET does not
        /// provide a built-in way to enumerate alternate data streams.
        /// </summary>
        public IReadOnlyList<string> Streams { get; set; } = Array.Empty<string>();
    }
}
