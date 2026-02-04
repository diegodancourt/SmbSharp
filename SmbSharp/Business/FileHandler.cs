using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmbSharp.Business.Interfaces;
using SmbSharp.Business.SmbClient;
using SmbSharp.Enums;
using SmbSharp.Infrastructure;

namespace SmbSharp.Business
{
    /// <summary>
    /// Implementation of IFileHandler that provides SMB/CIFS file operations across different platforms.
    /// Uses native UNC paths on Windows and smbclient on Linux.
    /// </summary>
    public class FileHandler : IFileHandler
    {
        private readonly ILogger<FileHandler> _logger;
        private readonly ISmbClientFileHandler _smbClientFileHandler;

        /// <summary>
        /// Creates a new FileHandler using Kerberos authentication.
        /// On Linux, requires a valid Kerberos ticket (kinit).
        /// </summary>
        /// <param name="loggerFactory">Optional logger factory for debug output. Pass null to disable logging.</param>
        /// <returns>A new FileHandler instance</returns>
        /// <exception cref="PlatformNotSupportedException">Thrown when running on unsupported platform</exception>
        /// <exception cref="InvalidOperationException">Thrown when smbclient is not available on Linux</exception>
        public static FileHandler CreateWithKerberos(ILoggerFactory? loggerFactory = null)
        {
            loggerFactory ??= new NullLoggerFactory();
            var processWrapper = new ProcessWrapper(loggerFactory.CreateLogger<ProcessWrapper>());
            var smbClientHandler = new SmbClientFileHandler(
                loggerFactory.CreateLogger<SmbClientFileHandler>(),
                processWrapper,
                useKerberos: true);

            return new FileHandler(loggerFactory.CreateLogger<FileHandler>(), smbClientHandler);
        }

        /// <summary>
        /// Creates a new FileHandler using username/password authentication.
        /// </summary>
        /// <param name="username">Username for authentication</param>
        /// <param name="password">Password for authentication</param>
        /// <param name="domain">Optional domain for authentication</param>
        /// <param name="loggerFactory">Optional logger factory for debug output. Pass null to disable logging.</param>
        /// <returns>A new FileHandler instance</returns>
        /// <exception cref="ArgumentException">Thrown when username or password is null or empty</exception>
        /// <exception cref="PlatformNotSupportedException">Thrown when running on unsupported platform</exception>
        /// <exception cref="InvalidOperationException">Thrown when smbclient is not available on Linux</exception>
        public static FileHandler CreateWithCredentials(string username, string password, string? domain = null, ILoggerFactory? loggerFactory = null)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Username cannot be null or empty", nameof(username));
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password cannot be null or empty", nameof(password));

            loggerFactory ??= new NullLoggerFactory();
            var processWrapper = new ProcessWrapper(loggerFactory.CreateLogger<ProcessWrapper>());
            var smbClientHandler = new SmbClientFileHandler(
                loggerFactory.CreateLogger<SmbClientFileHandler>(),
                processWrapper,
                useKerberos: false,
                username,
                password,
                domain);

            return new FileHandler(loggerFactory.CreateLogger<FileHandler>(), smbClientHandler);
        }

        /// <summary>
        /// Initializes a new instance of FileHandler with Kerberos authentication (requires kinit ticket).
        /// This constructor is used by dependency injection.
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">Thrown when running on unsupported platform (not Windows or Linux)</exception>
        /// <exception cref="InvalidOperationException">Thrown when smbclient is not available on Linux</exception>
        public FileHandler(ILogger<FileHandler> logger, ISmbClientFileHandler smbClientFileHandler)
        {
            _logger = logger;
            _smbClientFileHandler = smbClientFileHandler;
            ValidatePlatformAndDependencies();
        }

        private void ValidatePlatformAndDependencies()
        {
            // Check if running on supported platform (Windows or Linux)
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _logger.LogError("Unsupported platform: {Platform}", RuntimeInformation.OSDescription);
                throw new PlatformNotSupportedException(
                    "SmbSharp only supports Windows and Linux platforms. " +
                    $"Current platform: {RuntimeInformation.OSDescription}");
            }

            // On non-Windows platforms, verify smbclient is available
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!_smbClientFileHandler.IsSmbClientAvailable())
                {
                    _logger.LogError("smbclient is not installed or not available in PATH.");
                    throw new InvalidOperationException(
                        "smbclient is not installed or not available in PATH. " +
                        "On Linux, install it using: apt-get install smbclient (Debian/Ubuntu) " +
                        "or yum install samba-client (RHEL/CentOS)");
                }
            }
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<string>> EnumerateFilesAsync(string directory,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directory));

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _smbClientFileHandler.EnumerateFilesAsync(directory, cancellationToken);
            }

            // On Windows, use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
            return await Task.Run(IEnumerable<string> () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(directory))
                {
                    throw new DirectoryNotFoundException(
                        $"The directory {directory} could not be found or I don't have access to it");
                }

                return Directory.EnumerateFiles(directory)
                    .Select(Path.GetFileName)
                    .Where(f => !string.IsNullOrEmpty(f))!;
            }, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> FileExistsAsync(string fileName, string directory,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("File name cannot be null or empty", nameof(fileName));
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directory));

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _smbClientFileHandler.FileExistsAsync(fileName, directory, cancellationToken);
            }

            // On Windows, use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filePath = Path.Combine(directory, fileName);
                return File.Exists(filePath);
            }, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<Stream> ReadFileAsync(string directory, string fileName,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directory));
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("File name cannot be null or empty", nameof(fileName));

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _smbClientFileHandler.GetFileStreamAsync(directory, fileName, cancellationToken);
            }

            // On Windows, safely combine paths - wrap in Task.Run to avoid blocking
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var filePath = Path.Combine(directory, fileName);

                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException(
                        $"The file {filePath} could not be found or I don't have access to it");
                }

                return (Stream)new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                    FileOptions.Asynchronous);
            }, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> WriteFileAsync(string filePath, string content,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            return await WriteFileAsync(filePath, stream, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> WriteFileAsync(string filePath, Stream stream,
            CancellationToken cancellationToken = default)
        {
            return await WriteFileAsync(filePath, stream, FileWriteMode.Overwrite, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> WriteFileAsync(string filePath, Stream stream, FileWriteMode writeMode,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            // Reset stream position if seekable
            if (stream.CanSeek && stream.Position != 0)
            {
                stream.Position = 0;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // On Windows, use direct IO operations for UNC paths
                return await Task.Run(async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Map FileWriteMode to FileMode
                    FileMode fileMode = writeMode switch
                    {
                        FileWriteMode.CreateNew => FileMode.CreateNew,
                        FileWriteMode.Append => FileMode.Append,
                        _ => FileMode.Create
                    };

                    await using var fileStream = new FileStream(filePath, fileMode, FileAccess.Write, FileShare.None,
                        4096, FileOptions.Asynchronous);
                    await stream.CopyToAsync(fileStream, cancellationToken);
                    return true;
                }, cancellationToken);
            }

            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("Invalid file path - cannot determine directory", nameof(filePath));

            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentException("Invalid file path - cannot determine file name", nameof(filePath));

            return await _smbClientFileHandler.WriteFileAsync(directory, fileName, stream, writeMode,
                cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> CreateDirectoryAsync(string directoryPath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _smbClientFileHandler.CreateDirectoryAsync(directoryPath, cancellationToken);
            }

            // On Windows, use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                return true;
            }, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> MoveFileAsync(string sourceFilePath, string destinationFilePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
                throw new ArgumentException("Source file path cannot be null or empty", nameof(sourceFilePath));
            if (string.IsNullOrWhiteSpace(destinationFilePath))
                throw new ArgumentException("Destination file path cannot be null or empty",
                    nameof(destinationFilePath));

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // For smbclient, we need to download and re-upload since there's no native move command
                var sourceDir = Path.GetDirectoryName(sourceFilePath);
                if (string.IsNullOrEmpty(sourceDir))
                    throw new ArgumentException("Invalid source path - cannot determine directory",
                        nameof(sourceFilePath));

                var sourceFileName = Path.GetFileName(sourceFilePath);
                if (string.IsNullOrEmpty(sourceFileName))
                    throw new ArgumentException("Invalid source path - cannot determine file name",
                        nameof(sourceFilePath));

                var destDir = Path.GetDirectoryName(destinationFilePath);
                if (string.IsNullOrEmpty(destDir))
                    throw new ArgumentException("Invalid destination path - cannot determine directory",
                        nameof(destinationFilePath));

                var destFileName = Path.GetFileName(destinationFilePath);
                if (string.IsNullOrEmpty(destFileName))
                    throw new ArgumentException("Invalid destination path - cannot determine file name",
                        nameof(destinationFilePath));

                // Read source file
                await using var stream =
                    await _smbClientFileHandler.GetFileStreamAsync(sourceDir, sourceFileName, cancellationToken);

                // Write to destination
                await _smbClientFileHandler.WriteFileAsync(destDir, destFileName, stream, cancellationToken);

                // Delete source
                await _smbClientFileHandler.DeleteFileAsync(sourceDir, sourceFileName, cancellationToken);

                return true;
            }

            // On Windows, use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(sourceFilePath, destinationFilePath);
                return true;
            }, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            cancellationToken.ThrowIfCancellationRequested();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // On Windows, use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }

                    return true;
                }, cancellationToken);
            }

            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("Invalid file path - cannot determine directory", nameof(filePath));

            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentException("Invalid file path - cannot determine file name", nameof(filePath));

            return await _smbClientFileHandler.DeleteFileAsync(directory, fileName, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> CanConnectAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                return false;

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await _smbClientFileHandler.CanConnectAsync(directoryPath, cancellationToken);
            }

            // On Windows, use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
            return await Task.Run(() => Directory.Exists(directoryPath), cancellationToken);
        }
    }
}