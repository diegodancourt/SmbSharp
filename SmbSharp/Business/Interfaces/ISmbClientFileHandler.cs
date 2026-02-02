using SmbSharp.Enums;

namespace SmbSharp.Business.Interfaces
{
    /// <summary>
    /// Interface for SMB client file operations using the smbclient command-line tool.
    /// </summary>
    public interface ISmbClientFileHandler
    {
        /// <summary>
        /// Checks if the smbclient command-line tool is available on the system.
        /// </summary>
        /// <returns>True if smbclient is available; otherwise, false.</returns>
        bool IsSmbClientAvailable();

        /// <summary>
        /// Enumerates files in the specified SMB path.
        /// </summary>
        /// <param name="smbPath">The SMB path in UNC format (e.g., \\server\share\path).</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A collection of file names in the specified path.</returns>
        Task<IEnumerable<string>> EnumerateFilesAsync(string smbPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a file exists in the specified SMB directory.
        /// </summary>
        /// <param name="fileName">The name of the file to check.</param>
        /// <param name="smbPath">The SMB path in UNC format (e.g., \\server\share\path).</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>True if the file exists; otherwise, false.</returns>
        Task<bool> FileExistsAsync(string fileName, string smbPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a stream to read a file from the specified SMB path.
        /// </summary>
        /// <param name="smbPath">The SMB path in UNC format (e.g., \\server\share\path).</param>
        /// <param name="fileName">The name of the file to read.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A stream containing the file contents.</returns>
        Task<Stream> GetFileStreamAsync(string smbPath, string fileName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes a stream to a file in the specified SMB path.
        /// </summary>
        /// <param name="smbPath">The SMB path in UNC format (e.g., \\server\share\path).</param>
        /// <param name="fileName">The name of the file to write.</param>
        /// <param name="stream">The stream to write.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>True if the operation succeeded; otherwise, false.</returns>
        Task<bool> WriteFileAsync(string smbPath, string fileName, Stream stream,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes a stream to a file in the specified SMB path with the specified write mode.
        /// </summary>
        /// <param name="smbPath">The SMB path in UNC format (e.g., \\server\share\path).</param>
        /// <param name="fileName">The name of the file to write.</param>
        /// <param name="stream">The stream to write.</param>
        /// <param name="writeMode">The write mode (CreateNew, Overwrite, or Append).</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>True if the operation succeeded; otherwise, false.</returns>
        Task<bool> WriteFileAsync(string smbPath, string fileName, Stream stream,
            FileWriteMode writeMode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a file from the specified SMB path.
        /// </summary>
        /// <param name="smbPath">The SMB path in UNC format (e.g., \\server\share\path).</param>
        /// <param name="fileName">The name of the file to delete.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>True if the operation succeeded; otherwise, false.</returns>
        Task<bool> DeleteFileAsync(string smbPath, string fileName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a directory at the specified SMB path. This operation is idempotent - it does nothing if the directory already exists.
        /// </summary>
        /// <param name="smbPath">The SMB path in UNC format (e.g., \\server\share\path).</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>True if the operation succeeded; otherwise, false.</returns>
        Task<bool> CreateDirectoryAsync(string smbPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Tests connectivity to the specified SMB path.
        /// </summary>
        /// <param name="directoryPath">The SMB path in UNC format (e.g., \\server\share\path).</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>True if the connection was successful; otherwise, false.</returns>
        Task<bool> CanConnectAsync(string directoryPath, CancellationToken cancellationToken = default);
    }
}