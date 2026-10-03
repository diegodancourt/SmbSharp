using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmbSharp.Business.Interfaces;
using SmbSharp.Business.SmbClient;
using SmbSharp.Business.SmbClient.Session;
using SmbSharp.Enums;
using SmbSharp.Infrastructure;
using SmbSharp.Models;

namespace SmbSharp.Business
{
    /// <summary>
    /// Implementation of IFileHandler that provides SMB/CIFS file operations across different platforms.
    /// Uses native UNC paths on Windows (or smbclient via WSL if opted in) and smbclient on Linux/macOS.
    /// </summary>
    public class FileHandler : IFileHandler, IDisposable
    {
        private readonly ILogger<FileHandler> _logger;
        private readonly ISmbClientFileHandler _smbClientFileHandler;
        private readonly bool _useSmbClient;
        private readonly ISmbClientSessionPool? _ownedSessionPool;

        /// <summary>
        /// Creates a new FileHandler using Kerberos authentication.
        /// On Linux/macOS, requires a valid Kerberos ticket (kinit) and smbclient installed.
        /// On Windows, uses native UNC paths by default. Set useWsl to true to use smbclient via WSL instead.
        /// </summary>
        /// <param name="loggerFactory">Optional logger factory for debug output. Pass null to disable logging.</param>
        /// <param name="useWsl">When true on Windows, uses smbclient via WSL instead of native UNC paths. Ignored on Linux/macOS.</param>
        /// <param name="useSessionPool">When true, keeps a small pool of persistent, authenticated smbclient
        /// sessions open per share instead of re-authenticating on every call. Strongly recommended.</param>
        /// <param name="sessionPoolSize">Number of persistent sessions kept per share when useSessionPool is true.</param>
        /// <param name="sessionIdleTimeout">How long an idle session may sit before disposal when useSessionPool is true.</param>
        /// <param name="sessionCommandTimeout">Maximum duration of one pooled smbclient command.</param>
        /// <param name="wslDistribution">Optional WSL distribution name; null uses the default distribution.</param>
        /// <returns>A new FileHandler instance</returns>
        /// <exception cref="PlatformNotSupportedException">Thrown when running on unsupported platform</exception>
        /// <exception cref="InvalidOperationException">Thrown when smbclient is not available on Linux/macOS (or via WSL when useWsl is true)</exception>
        public static FileHandler CreateWithKerberos(ILoggerFactory? loggerFactory = null, bool useWsl = false,
            bool useSessionPool = false, int sessionPoolSize = 3, TimeSpan? sessionIdleTimeout = null,
            TimeSpan? sessionCommandTimeout = null, string? wslDistribution = null)
        {
            loggerFactory ??= new NullLoggerFactory();
            var processWrapper = new ProcessWrapper(loggerFactory.CreateLogger<ProcessWrapper>());
            var sessionPool = CreateSessionPoolIfEnabled(loggerFactory, useSessionPool, useKerberos: true,
                username: null, password: null, domain: null, useWsl, sessionPoolSize, sessionIdleTimeout,
                sessionCommandTimeout, wslDistribution);
            try
            {
                var smbClientHandler = new SmbClientFileHandler(
                    loggerFactory.CreateLogger<SmbClientFileHandler>(),
                    processWrapper,
                    useKerberos: true,
                    useWsl: useWsl,
                    sessionPool: sessionPool,
                    wslDistribution: wslDistribution);
                return new FileHandler(loggerFactory.CreateLogger<FileHandler>(), smbClientHandler, useWsl, sessionPool);
            }
            catch
            {
                sessionPool?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates a new FileHandler using username/password authentication.
        /// On Linux/macOS, requires smbclient installed.
        /// On Windows, uses native UNC paths by default. Set useWsl to true to use smbclient via WSL instead.
        /// </summary>
        /// <param name="username">Username for authentication</param>
        /// <param name="password">Password for authentication</param>
        /// <param name="domain">Optional domain for authentication</param>
        /// <param name="loggerFactory">Optional logger factory for debug output. Pass null to disable logging.</param>
        /// <param name="useWsl">When true on Windows, uses smbclient via WSL instead of native UNC paths. Ignored on Linux/macOS.</param>
        /// <param name="useSessionPool">When true, keeps a small pool of persistent, authenticated smbclient
        /// sessions open per share instead of re-authenticating on every call.</param>
        /// <param name="sessionPoolSize">Number of persistent sessions kept per share when useSessionPool is true.</param>
        /// <param name="sessionIdleTimeout">How long an idle session may sit before disposal when useSessionPool is true.</param>
        /// <param name="sessionCommandTimeout">Maximum duration of one pooled smbclient command.</param>
        /// <param name="wslDistribution">Optional WSL distribution name; null uses the default distribution.</param>
        /// <returns>A new FileHandler instance</returns>
        /// <exception cref="ArgumentException">Thrown when username or password is null or empty</exception>
        /// <exception cref="PlatformNotSupportedException">Thrown when running on unsupported platform</exception>
        /// <exception cref="InvalidOperationException">Thrown when smbclient is not available on Linux/macOS (or via WSL when useWsl is true)</exception>
        public static FileHandler CreateWithCredentials(string username, string password, string? domain = null,
            ILoggerFactory? loggerFactory = null, bool useWsl = false, bool useSessionPool = false,
            int sessionPoolSize = 3, TimeSpan? sessionIdleTimeout = null,
            TimeSpan? sessionCommandTimeout = null, string? wslDistribution = null)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Username cannot be null or empty", nameof(username));
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password cannot be null or empty", nameof(password));

            loggerFactory ??= new NullLoggerFactory();
            var processWrapper = new ProcessWrapper(loggerFactory.CreateLogger<ProcessWrapper>());
            var sessionPool = CreateSessionPoolIfEnabled(loggerFactory, useSessionPool, useKerberos: false,
                username, password, domain, useWsl, sessionPoolSize, sessionIdleTimeout,
                sessionCommandTimeout, wslDistribution);
            try
            {
                var smbClientHandler = new SmbClientFileHandler(
                    loggerFactory.CreateLogger<SmbClientFileHandler>(),
                    processWrapper,
                    useKerberos: false,
                    username,
                    password,
                    domain,
                    useWsl: useWsl,
                    sessionPool: sessionPool,
                    wslDistribution: wslDistribution);
                return new FileHandler(loggerFactory.CreateLogger<FileHandler>(), smbClientHandler, useWsl, sessionPool);
            }
            catch
            {
                sessionPool?.Dispose();
                throw;
            }
        }

        private static ISmbClientSessionPool? CreateSessionPoolIfEnabled(ILoggerFactory loggerFactory,
            bool useSessionPool, bool useKerberos, string? username, string? password, string? domain, bool useWsl,
            int sessionPoolSize, TimeSpan? sessionIdleTimeout, TimeSpan? sessionCommandTimeout, string? wslDistribution)
        {
            if (!useSessionPool)
                return null;

            var processFactory = new InteractiveProcessFactory(loggerFactory);
            return new SmbClientSessionPool(loggerFactory, processFactory, useKerberos, username, password, domain,
                useWsl, sessionPoolSize, sessionIdleTimeout, commandTimeout: sessionCommandTimeout,
                wslDistribution: wslDistribution);
        }


        /// <summary>
        /// Initializes a new instance of FileHandler.
        /// This constructor is used by dependency injection.
        /// On Windows, uses native UNC paths. On Linux/macOS, uses smbclient.
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">Thrown when running on unsupported platform</exception>
        /// <exception cref="InvalidOperationException">Thrown when smbclient is not available on Linux/macOS</exception>
        public FileHandler(ILogger<FileHandler> logger, ISmbClientFileHandler smbClientFileHandler)
            : this(logger, smbClientFileHandler, useWsl: false)
        {
        }

        /// <summary>
        /// Initializes a new instance of FileHandler with optional WSL support.
        /// On Windows with useWsl=false, uses native UNC paths.
        /// On Windows with useWsl=true, uses smbclient via WSL.
        /// On Linux/macOS, uses smbclient directly (useWsl is ignored).
        /// </summary>
        /// <param name="logger">Logger instance</param>
        /// <param name="smbClientFileHandler">The smbclient file handler</param>
        /// <param name="useWsl">When true on Windows, uses smbclient via WSL instead of native UNC paths</param>
        /// <exception cref="PlatformNotSupportedException">Thrown when running on unsupported platform</exception>
        /// <exception cref="InvalidOperationException">Thrown when smbclient is not available on Linux/macOS (or via WSL when useWsl is true)</exception>
        public FileHandler(ILogger<FileHandler> logger, ISmbClientFileHandler smbClientFileHandler, bool useWsl)
            : this(logger, smbClientFileHandler, useWsl, null)
        {
        }

        internal FileHandler(ILogger<FileHandler> logger, ISmbClientFileHandler smbClientFileHandler, bool useWsl,
            ISmbClientSessionPool? ownedSessionPool)
        {
            _logger = logger;
            _smbClientFileHandler = smbClientFileHandler;
            _ownedSessionPool = ownedSessionPool;

            // On Linux/macOS, always use smbclient. On Windows, only if useWsl is opted in.
            _useSmbClient = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || useWsl;

            ValidatePlatformAndDependencies();
        }

        private void ValidatePlatformAndDependencies()
        {
            // Check if running on supported platform (Windows, Linux, or macOS)
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                !RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
                !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                _logger.LogError("Unsupported platform: {platform}", RuntimeInformation.OSDescription);
                throw new PlatformNotSupportedException(
                    "SmbSharp only supports Windows, Linux, and macOS platforms. " +
                    $"Current platform: {RuntimeInformation.OSDescription}");
            }

            if (_useSmbClient)
            {
                // Verify smbclient is available
                if (!_smbClientFileHandler.IsSmbClientAvailable())
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        _logger.LogError("smbclient is not available through WSL.");
                        throw new InvalidOperationException(
                            "smbclient is not available through WSL. " +
                            "Ensure WSL is installed and smbclient is available inside your WSL distribution: " +
                            "wsl apt-get install smbclient");
                    }

                    _logger.LogError("smbclient is not installed or not available in PATH.");
                    throw new InvalidOperationException(
                        "smbclient is not installed or not available in PATH. " +
                        "Install it using: apt-get install smbclient (Debian/Ubuntu), " +
                        "yum install samba-client (RHEL/CentOS), or brew install samba (macOS)");
                }
            }
        }

        // Splits an SMB UNC-style path (e.g. "//server/share/folder/file.txt" or
        // "\\server\share\folder\file.txt") into its directory and file name components.
        //
        // Path.GetDirectoryName/Path.GetFileName cannot be used here: on Linux/macOS they treat
        // "//server/share/folder/file.txt" as a POSIX path and collapse the leading "//" down to a
        // single "/" (e.g. producing "/server/share/folder"), which breaks SmbClientFileHandler's UNC
        // path parsing (see GitHub issue #4). Splitting on the last separator manually avoids this.
        private static (string directory, string fileName) SplitSmbFilePath(string filePath)
        {
            var normalized = filePath.Replace('\\', '/');
            var lastSeparatorIndex = normalized.LastIndexOf('/');

            if (lastSeparatorIndex < 0)
                return (string.Empty, normalized);

            var directory = normalized[..lastSeparatorIndex];
            var fileName = normalized[(lastSeparatorIndex + 1)..];

            return (directory, fileName);
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<string>> EnumerateFilesAsync(string directory,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directory));

            if (_useSmbClient)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _smbClientFileHandler.EnumerateFilesAsync(directory, cancellationToken);
            }

            // Use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
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
                    .Where(f => !string.IsNullOrEmpty(f))
                    .ToList()!;
            }, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<string>> EnumerateDirectoriesAsync(string directory,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directory));

            if (_useSmbClient)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _smbClientFileHandler.EnumerateDirectoriesAsync(directory, cancellationToken);
            }

            // Use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
            return await Task.Run(IEnumerable<string> () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(directory))
                {
                    throw new DirectoryNotFoundException(
                        $"The directory {directory} could not be found or I don't have access to it");
                }

                return Directory.EnumerateDirectories(directory)
                    .Select(Path.GetFileName)
                    .Where(f => !string.IsNullOrEmpty(f))
                    .ToList()!;
            }, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<SmbFileInfo> GetFileInfoAsync(string directory, string fileName,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directory));
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("File name cannot be null or empty", nameof(fileName));

            if (_useSmbClient)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _smbClientFileHandler.GetFileInfoAsync(directory, fileName, cancellationToken);
            }

            // Use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullPath = Path.Combine(directory, fileName);
                var isDirectory = Directory.Exists(fullPath);
                if (!isDirectory && !File.Exists(fullPath))
                {
                    throw new FileNotFoundException(
                        $"The file {fullPath} could not be found or I don't have access to it");
                }

                var attributes = isDirectory
                    ? new DirectoryInfo(fullPath).Attributes
                    : new System.IO.FileInfo(fullPath).Attributes;

                // .NET has no built-in way to enumerate NTFS alternate data streams, and no distinct
                // "change time" concept, so ChangeTime falls back to the write time.
                return new SmbFileInfo
                {
                    AlternateName = null,
                    CreateTime = isDirectory ? Directory.GetCreationTime(fullPath) : File.GetCreationTime(fullPath),
                    AccessTime = isDirectory
                        ? Directory.GetLastAccessTime(fullPath)
                        : File.GetLastAccessTime(fullPath),
                    WriteTime = isDirectory ? Directory.GetLastWriteTime(fullPath) : File.GetLastWriteTime(fullPath),
                    ChangeTime = isDirectory ? Directory.GetLastWriteTime(fullPath) : File.GetLastWriteTime(fullPath),
                    Attributes = attributes.ToString(),
                    Streams = Array.Empty<string>()
                };
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

            if (_useSmbClient)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _smbClientFileHandler.FileExistsAsync(fileName, directory, cancellationToken);
            }

            // Use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
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

            if (_useSmbClient)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _smbClientFileHandler.GetFileStreamAsync(directory, fileName, cancellationToken);
            }

            // Use direct IO - safely combine paths - wrap in Task.Run to avoid blocking
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

            if (!_useSmbClient)
            {
                // Use direct IO operations for UNC paths
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

            var (directory, fileName) = SplitSmbFilePath(filePath);
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("Invalid file path - cannot determine directory", nameof(filePath));
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

            if (_useSmbClient)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _smbClientFileHandler.CreateDirectoryAsync(directoryPath, cancellationToken);
            }

            // Use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
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

            if (_useSmbClient)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (sourceDir, sourceFileName) = SplitSmbFilePath(sourceFilePath);
                if (string.IsNullOrEmpty(sourceDir))
                    throw new ArgumentException("Invalid source path - cannot determine directory",
                        nameof(sourceFilePath));
                if (string.IsNullOrEmpty(sourceFileName))
                    throw new ArgumentException("Invalid source path - cannot determine file name",
                        nameof(sourceFilePath));

                var (destDir, destFileName) = SplitSmbFilePath(destinationFilePath);
                if (string.IsNullOrEmpty(destDir))
                    throw new ArgumentException("Invalid destination path - cannot determine directory",
                        nameof(destinationFilePath));
                if (string.IsNullOrEmpty(destFileName))
                    throw new ArgumentException("Invalid destination path - cannot determine file name",
                        nameof(destinationFilePath));

                if (GetShareRoot(sourceFilePath).Equals(GetShareRoot(destinationFilePath), StringComparison.OrdinalIgnoreCase) &&
                    _smbClientFileHandler is ISmbClientFileHandlerMove moveHandler)
                {
                    if (await _smbClientFileHandler.FileExistsAsync(destFileName, destDir, cancellationToken))
                        throw new IOException($"Destination file already exists: {destinationFilePath}");

                    await moveHandler.RenameFileAsync(sourceDir, sourceFileName, destDir, destFileName, cancellationToken);
                    return true;
                }

                // Cross-share moves must copy data. CreateNew avoids clobbering a destination that
                // was already present; if source deletion fails, retain both copies rather than risk
                // deleting a destination we did not create or losing the only remaining copy.
                await using var stream =
                    await _smbClientFileHandler.GetFileStreamAsync(sourceDir, sourceFileName, cancellationToken);
                await _smbClientFileHandler.WriteFileAsync(destDir, destFileName, stream,
                    FileWriteMode.CreateNew, cancellationToken);
                await _smbClientFileHandler.DeleteFileAsync(sourceDir, sourceFileName, cancellationToken);
                return true;
            }

            // Use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
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

            if (!_useSmbClient)
            {
                // Use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
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

            var (directory, fileName) = SplitSmbFilePath(filePath);
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("Invalid file path - cannot determine directory", nameof(filePath));
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentException("Invalid file path - cannot determine file name", nameof(filePath));

            return await _smbClientFileHandler.DeleteFileAsync(directory, fileName, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> CanConnectAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                return false;

            if (_useSmbClient)
            {
                return await _smbClientFileHandler.CanConnectAsync(directoryPath, cancellationToken);
            }

            // Use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
            return await Task.Run(() => Directory.Exists(directoryPath), cancellationToken);
        }

        /// <summary>Disposes any persistent session pool created by this factory-created handler.</summary>
        public void Dispose() => _ownedSessionPool?.Dispose();

        private static string GetShareRoot(string path)
        {
            var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length < 2 ? string.Empty : $"//{segments[0]}/{segments[1]}";
        }
    }
}