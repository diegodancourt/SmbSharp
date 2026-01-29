namespace SmbSharp.Enums
{
    /// <summary>
    /// Specifies the behavior when writing a file that may already exist.
    /// </summary>
    public enum FileWriteMode
    {
        /// <summary>
        /// Creates a new file or overwrites an existing file (default behavior).
        /// </summary>
        Overwrite,

        /// <summary>
        /// Creates a new file only if it doesn't exist. Throws an exception if the file already exists.
        /// </summary>
        CreateNew,

        /// <summary>
        /// Appends data to an existing file, or creates a new file if it doesn't exist.
        /// </summary>
        Append
    }
}
