namespace SmbSharp.Business.Interfaces
{
    /// <summary>
    /// Provides an interface for file operations on SMB/CIFS shares across different platforms.
    /// Supports both Kerberos authentication and username/password authentication.
    /// </summary>
    public interface IFileHandler
    {
        /// <summary>
        /// Enumerates all files in the specified directory on an SMB share.
        /// </summary>
        /// <param name="directory">The SMB directory path (e.g., "//server/share/path" or "\\server\share\path")</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>A collection of file names in the directory (not full paths, just file names)</returns>
        /// <exception cref="DirectoryNotFoundException">Thrown when the directory does not exist or is not accessible</exception>
        /// <exception cref="IOException">Thrown when the SMB operation fails</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
        Task<IEnumerable<string>> EnumerateFilesAsync(string directory, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a file exists in the specified directory on an SMB share.
        /// </summary>
        /// <param name="fileName">The name of the file to check (not full path, just file name)</param>
        /// <param name="directory">The SMB directory path (e.g., "//server/share/path" or "\\server\share\path")</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>True if the file exists, false otherwise</returns>
        /// <exception cref="DirectoryNotFoundException">Thrown when the directory does not exist or is not accessible</exception>
        /// <exception cref="IOException">Thrown when the SMB operation fails</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
        Task<bool> FileExistsAsync(string fileName, string directory, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a file for reading from an SMB share and returns a stream.
        /// The caller is responsible for disposing the returned stream.
        /// </summary>
        /// <param name="directory">The SMB directory path containing the file</param>
        /// <param name="filePath">The name of the file to read</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>A stream containing the file contents. The stream must be disposed by the caller.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the file does not exist or is not accessible</exception>
        /// <exception cref="IOException">Thrown when the SMB operation fails</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
        /// <remarks>
        /// On Windows, returns a FileStream to the UNC path directly.
        /// On non-Windows platforms, downloads the file to a temporary location and returns a FileStream with DeleteOnClose.
        /// </remarks>
        Task<Stream> ReadFileAsync(string directory, string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes a string to a file on an SMB share, creating or overwriting the file.
        /// The content is encoded as UTF-8.
        /// </summary>
        /// <param name="filePath">The full SMB path to the file (e.g., "//server/share/path/file.txt")</param>
        /// <param name="content">The string content to write to the file</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>True if the operation succeeded, false otherwise</returns>
        /// <exception cref="IOException">Thrown when the SMB operation fails</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
        Task<bool> WriteFileAsync(string filePath, string content, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes the contents of a stream to a file on an SMB share, creating or overwriting the file.
        /// </summary>
        /// <param name="filePath">The full SMB path to the file (e.g., "//server/share/path/file.txt")</param>
        /// <param name="stream">The stream containing the data to write. If the stream is seekable and not at position 0, it will be reset to the beginning.</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>True if the operation succeeded, false otherwise</returns>
        /// <exception cref="IOException">Thrown when the SMB operation fails</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
        /// <remarks>
        /// On non-Windows platforms, the stream is copied to a temporary file before being uploaded via smbclient.
        /// If the stream is seekable, it will be reset to position 0 before reading.
        /// </remarks>
        Task<bool> WriteFileAsync(string filePath, Stream stream, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes the contents of a stream to a file on an SMB share with the specified write mode.
        /// </summary>
        /// <param name="filePath">The full SMB path to the file (e.g., "//server/share/path/file.txt")</param>
        /// <param name="stream">The stream containing the data to write. If the stream is seekable and not at position 0, it will be reset to the beginning.</param>
        /// <param name="writeMode">Specifies the behavior when the file already exists</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>True if the operation succeeded, false otherwise</returns>
        /// <exception cref="IOException">Thrown when the SMB operation fails or file already exists with CreateNew mode</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
        /// <remarks>
        /// On non-Windows platforms, the stream is copied to a temporary file before being uploaded via smbclient.
        /// If the stream is seekable, it will be reset to position 0 before reading.
        /// Append mode is only supported on Windows; on Linux it will download, append, and re-upload the file.
        /// </remarks>
        Task<bool> WriteFileAsync(string filePath, Stream stream, Enums.FileWriteMode writeMode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a directory on an SMB share. Does nothing if the directory already exists.
        /// </summary>
        /// <param name="directoryPath">The full SMB path to the directory to create (e.g., "//server/share/path/newdir")</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>True if the operation succeeded, false otherwise</returns>
        /// <exception cref="ArgumentException">Thrown when the directory path is empty or invalid</exception>
        /// <exception cref="IOException">Thrown when the SMB operation fails</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
        Task<bool> CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Moves a file from one location to another on SMB shares.
        /// </summary>
        /// <param name="sourceFilePath">The full SMB path to the source file</param>
        /// <param name="destinationFilePath">The full SMB path to the destination file</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>True if the operation succeeded, false otherwise</returns>
        /// <exception cref="FileNotFoundException">Thrown when the source file does not exist</exception>
        /// <exception cref="IOException">Thrown when the SMB operation fails</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
        /// <remarks>
        /// <para>
        /// <strong>Windows:</strong> Uses File.Move directly - efficient, atomic operation that just updates file metadata.
        /// </para>
        /// <para>
        /// <strong>Linux:</strong> Performs a copy-then-delete operation since smbclient has no native move command.
        /// This means:
        /// - The file is downloaded to a temporary location
        /// - Then uploaded to the destination
        /// - Then deleted from the source
        /// - This requires 2x the file size in temporary disk space
        /// - Network transfer time is 2x (download + upload)
        /// - The operation is NOT atomic - if it fails midway, you may have copies in both locations
        /// - For large files, this can be slow and resource-intensive
        /// </para>
        /// <para>
        /// <strong>Recommendation:</strong> If you're moving large files on Linux, consider using alternative approaches
        /// or be aware of the performance implications.
        /// </para>
        /// </remarks>
        Task<bool> MoveFileAsync(string sourceFilePath, string destinationFilePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a file from an SMB share. Does nothing if the file does not exist.
        /// </summary>
        /// <param name="filePath">The full SMB path to the file to delete (e.g., "//server/share/path/file.txt")</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>True if the operation succeeded, false otherwise</returns>
        /// <exception cref="IOException">Thrown when the SMB operation fails</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
        Task<bool> DeleteFileAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Tests connectivity to an SMB share or directory.
        /// </summary>
        /// <param name="directoryPath">The SMB path to test (e.g., "//server/share" or "//server/share/path")</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>True if the connection succeeds and the path is accessible, false otherwise</returns>
        /// <remarks>
        /// On Windows, checks if the directory exists.
        /// On non-Windows platforms, attempts to list files using smbclient.
        /// This method does not throw exceptions; it returns false on failure.
        /// </remarks>
        Task<bool> CanConnectAsync(string directoryPath, CancellationToken cancellationToken = default);
    }
}