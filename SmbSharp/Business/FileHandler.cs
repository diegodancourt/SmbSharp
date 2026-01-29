using System.Runtime.InteropServices;
using SmbSharp.Business.SmbClient;
using SmbSharp.Interfaces;

namespace SmbSharp.Business
{
    /// <summary>
    /// Implementation of IFileHandler that provides SMB/CIFS file operations across different platforms.
    /// Uses native UNC paths on Windows and smbclient on Linux.
    /// </summary>
    public class FileHandler : IFileHandler
    {
        private readonly string? _username;
        private readonly string? _password;
        private readonly string? _domain;

        private readonly bool _useKerberos;

        // Cache for smbclient availability check
        private static bool? _smbClientAvailable;
        private static readonly object _smbClientCheckLock = new();

        /// <summary>
        /// Initializes a new instance of FileHandler with Kerberos authentication (requires kinit ticket).
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">Thrown when running on unsupported platform (not Windows or Linux)</exception>
        /// <exception cref="InvalidOperationException">Thrown when smbclient is not available on Linux</exception>
        public FileHandler()
        {
            ValidatePlatformAndDependencies();
            _useKerberos = true;
        }

        /// <summary>
        /// Initializes a new instance of FileHandler with username/password authentication.
        /// </summary>
        /// <param name="username">The username for SMB authentication</param>
        /// <param name="password">The password for SMB authentication</param>
        /// <param name="domain">The domain for SMB authentication (optional)</param>
        /// <exception cref="ArgumentException">Thrown when username or password is null or empty</exception>
        /// <exception cref="PlatformNotSupportedException">Thrown when running on unsupported platform (not Windows or Linux)</exception>
        /// <exception cref="InvalidOperationException">Thrown when smbclient is not available on Linux</exception>
        public FileHandler(string username, string password, string domain)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Username cannot be null or empty", nameof(username));
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password cannot be null or empty", nameof(password));

            ValidatePlatformAndDependencies();
            _useKerberos = false;
            _username = username;
            _password = password;
            _domain = domain;
        }

        private static void ValidatePlatformAndDependencies()
        {
            // Check if running on supported platform (Windows or Linux)
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                throw new PlatformNotSupportedException(
                    "SmbSharp only supports Windows and Linux platforms. " +
                    $"Current platform: {RuntimeInformation.OSDescription}");
            }

            // On non-Windows platforms, verify smbclient is available
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!IsSmbClientAvailable())
                {
                    throw new InvalidOperationException(
                        "smbclient is not installed or not available in PATH. " +
                        "On Linux, install it using: apt-get install smbclient (Debian/Ubuntu) " +
                        "or yum install samba-client (RHEL/CentOS)");
                }
            }
        }

        private static bool IsSmbClientAvailable()
        {
            // Use cached result if available
            if (_smbClientAvailable.HasValue)
                return _smbClientAvailable.Value;

            lock (_smbClientCheckLock)
            {
                // Double-check after acquiring lock
                if (_smbClientAvailable.HasValue)
                    return _smbClientAvailable.Value;

                try
                {
                    var processStartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "smbclient",
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = System.Diagnostics.Process.Start(processStartInfo);
                    if (process == null)
                    {
                        _smbClientAvailable = false;
                        return false;
                    }

                    process.WaitForExit();
                    _smbClientAvailable = process.ExitCode == 0;
                    return _smbClientAvailable.Value;
                }
                catch
                {
                    _smbClientAvailable = false;
                    return false;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<string>> EnumerateFilesAsync(string directory, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directory));

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await SmbClientFileHandler.EnumerateFilesAsync(directory, _useKerberos, _username, _password,
                    _domain, cancellationToken);
            }

            // On Windows, use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(directory))
                {
                    throw new DirectoryNotFoundException(
                        $"The directory {directory} could not be found or I don't have access to it");
                }

                return (IEnumerable<string>)Directory.EnumerateFiles(directory)
                    .Select(Path.GetFileName)
                    .Where(f => !string.IsNullOrEmpty(f));
            }, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<Stream> ReadFileAsync(string directory, string fileName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directory));
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("File name cannot be null or empty", nameof(fileName));

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await SmbClientFileHandler.GetFileStreamAsync(directory, fileName, _useKerberos, _username,
                    _password, _domain, cancellationToken);
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

                return (Stream)new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            }, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> WriteFileAsync(string filePath, string content, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            return await WriteFileAsync(filePath, stream, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> WriteFileAsync(string filePath, Stream stream, CancellationToken cancellationToken = default)
        {
            return await WriteFileAsync(filePath, stream, Enums.FileWriteMode.Overwrite, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> WriteFileAsync(string filePath, Stream stream, Enums.FileWriteMode writeMode, CancellationToken cancellationToken = default)
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

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var directory = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(directory))
                    throw new ArgumentException("Invalid file path - cannot determine directory", nameof(filePath));

                var fileName = Path.GetFileName(filePath);
                if (string.IsNullOrEmpty(fileName))
                    throw new ArgumentException("Invalid file path - cannot determine file name", nameof(filePath));

                return await SmbClientFileHandler.WriteFileAsync(directory, fileName, stream, writeMode, _useKerberos, _username,
                    _password, _domain, cancellationToken);
            }

            // On Windows, use direct IO operations for UNC paths
            return await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Map FileWriteMode to FileMode
                FileMode fileMode = writeMode switch
                {
                    Enums.FileWriteMode.CreateNew => FileMode.CreateNew,
                    Enums.FileWriteMode.Append => FileMode.Append,
                    _ => FileMode.Create
                };

                await using var fileStream = new FileStream(filePath, fileMode, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
                await stream.CopyToAsync(fileStream, cancellationToken);
                return true;
            }, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await SmbClientFileHandler.CreateDirectoryAsync(directoryPath, _useKerberos, _username,
                    _password, _domain, cancellationToken);
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
        public async Task<bool> CanConnectAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                return false;

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await SmbClientFileHandler.CanConnectAsync(directoryPath, _useKerberos, _username, _password,
                    _domain, cancellationToken);
            }

            // On Windows, use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
            return await Task.Run(() => Directory.Exists(directoryPath), cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> MoveFileAsync(string sourceFilePath, string destinationFilePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
                throw new ArgumentException("Source file path cannot be null or empty", nameof(sourceFilePath));
            if (string.IsNullOrWhiteSpace(destinationFilePath))
                throw new ArgumentException("Destination file path cannot be null or empty", nameof(destinationFilePath));

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // For smbclient, we need to download and re-upload since there's no native move command
                var sourceDir = Path.GetDirectoryName(sourceFilePath);
                if (string.IsNullOrEmpty(sourceDir))
                    throw new ArgumentException("Invalid source path - cannot determine directory", nameof(sourceFilePath));

                var sourceFileName = Path.GetFileName(sourceFilePath);
                if (string.IsNullOrEmpty(sourceFileName))
                    throw new ArgumentException("Invalid source path - cannot determine file name", nameof(sourceFilePath));

                var destDir = Path.GetDirectoryName(destinationFilePath);
                if (string.IsNullOrEmpty(destDir))
                    throw new ArgumentException("Invalid destination path - cannot determine directory", nameof(destinationFilePath));

                var destFileName = Path.GetFileName(destinationFilePath);
                if (string.IsNullOrEmpty(destFileName))
                    throw new ArgumentException("Invalid destination path - cannot determine file name", nameof(destinationFilePath));

                // Read source file
                await using var stream = await SmbClientFileHandler.GetFileStreamAsync(sourceDir, sourceFileName, _useKerberos,
                    _username, _password, _domain, cancellationToken);

                // Write to destination
                await SmbClientFileHandler.WriteFileAsync(destDir, destFileName, stream, _useKerberos, _username,
                    _password, _domain, cancellationToken);

                // Delete source
                await SmbClientFileHandler.DeleteFileAsync(sourceDir, sourceFileName, _useKerberos, _username, _password,
                    _domain, cancellationToken);

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

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var directory = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(directory))
                    throw new ArgumentException("Invalid file path - cannot determine directory", nameof(filePath));

                var fileName = Path.GetFileName(filePath);
                if (string.IsNullOrEmpty(fileName))
                    throw new ArgumentException("Invalid file path - cannot determine file name", nameof(filePath));

                return await SmbClientFileHandler.DeleteFileAsync(directory, fileName, _useKerberos, _username,
                    _password, _domain, cancellationToken);
            }

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
    }
}