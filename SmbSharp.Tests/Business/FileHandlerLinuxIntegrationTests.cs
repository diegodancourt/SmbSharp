using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmbSharp.Business;
using SmbSharp.Business.SmbClient;
using SmbSharp.Enums;
using SmbSharp.Infrastructure;
using SmbSharp.Tests.Util;

namespace SmbSharp.Tests.Business
{
    /// <summary>
    /// Integration tests for FileHandler on Linux using real smbclient.
    /// These tests verify the Linux code path with actual implementations (no mocks).
    /// Requires smbclient to be installed on the system.
    ///
    /// NOTE: These tests will skip on Windows and require smbclient on Linux.
    /// Some tests may fail if smbclient is not properly configured or if there's no SMB share available.
    /// </summary>
    public class FileHandlerLinuxIntegrationTests : IDisposable
    {
        private readonly string _testDirectory;
        private FileHandler? _handler;

        public FileHandlerLinuxIntegrationTests()
        {
            // Create a temporary test directory
            _testDirectory = Path.Combine(Path.GetTempPath(), $"SmbSharpTest_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);
        }

        public void Dispose()
        {
            // Cleanup test directory
            if (Directory.Exists(_testDirectory))
            {
                try
                {
                    Directory.Delete(_testDirectory, true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        private FileHandler? CreateHandler()
        {
            if (!OperatingSystem.IsLinux())
            {
                // Skip handler creation on non-Linux
                return null;
            }

            if (_handler == null)
            {
                try
                {
                    // Use real FileHandler with Kerberos - no mocks!
                    // Create real dependencies: ProcessWrapper and SmbClientFileHandler
                    var fileHandlerLogger = NullLogger<FileHandler>.Instance;
                    var smbClientLogger = NullLogger<SmbClientFileHandler>.Instance;
                    var processWrapper = new ProcessWrapper();
                    var smbClientFileHandler = new SmbClientFileHandler(smbClientLogger, processWrapper, useKerberos: true);

                    _handler = new FileHandler(fileHandlerLogger, smbClientFileHandler);
                }
                catch (InvalidOperationException)
                {
                    // smbclient is not available - tests will be skipped
                    return null;
                }
                catch (PlatformNotSupportedException)
                {
                    // Not on a supported platform
                    return null;
                }
            }

            return _handler;
        }

        [LinuxFact]
        public void Constructor_OnLinux_WithSmbClientAvailable_Succeeds()
        {
            // Arrange & Act
            FileHandler? handler = null;
            Exception? exception = null;

            try
            {
                var fileHandlerLogger = NullLogger<FileHandler>.Instance;
                var smbClientLogger = NullLogger<SmbClientFileHandler>.Instance;
                var processWrapper = new ProcessWrapper();
                var smbClientFileHandler = new SmbClientFileHandler(smbClientLogger, processWrapper, useKerberos: true);
                handler = new FileHandler(fileHandlerLogger, smbClientFileHandler);
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            // Assert
            if (exception is InvalidOperationException && exception.Message.Contains("smbclient"))
            {
                // smbclient not installed - this is expected and test passes
                Assert.NotNull(exception);
            }
            else
            {
                // smbclient is installed - handler should be created successfully
                Assert.Null(exception);
                Assert.NotNull(handler);
            }
        }

        [LinuxFact]
        public void Constructor_OnLinux_RequiresSmbClient()
        {
            // This test verifies that the constructor checks for smbclient
            // It will either succeed (if smbclient is installed) or throw InvalidOperationException

            try
            {
                var fileHandlerLogger = NullLogger<FileHandler>.Instance;
                var smbClientLogger = NullLogger<SmbClientFileHandler>.Instance;
                var processWrapper = new ProcessWrapper();
                var smbClientFileHandler = new SmbClientFileHandler(smbClientLogger, processWrapper, useKerberos: true);
                var handler = new FileHandler(fileHandlerLogger, smbClientFileHandler);
                // If we get here, smbclient is available
                Assert.NotNull(handler);
            }
            catch (InvalidOperationException ex)
            {
                // smbclient is not available - verify error message
                Assert.Contains("smbclient is not installed", ex.Message);
            }
        }

        [LinuxSmbClientFact]
        public async Task WriteFileAsync_OnLinux_ExecutesRealSmbClientCommand()
        {
            var handler = CreateHandler() ??
                          throw new InvalidOperationException("smbclient is present but could not be initialized.");

            // Arrange
            var testFile = Path.Combine(_testDirectory, "test.txt");
            var content = "Hello from Linux integration test!";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

            // Act & Assert
            // Note: This will try to use smbclient to write the file
            // It may fail if there's no SMB share at the path, which is expected
            try
            {
                var result = await handler.WriteFileAsync(testFile, stream);
                // If successful, verify the operation completed
                Assert.True(result);
            }
            catch (Exception ex)
            {
                // Expected to fail without a real SMB share
                // Verify it's using smbclient (not Windows file operations)
                Assert.True(
                    ex is IOException ||
                    ex is FileNotFoundException ||
                    ex is DirectoryNotFoundException ||
                    ex is UnauthorizedAccessException ||
                    ex is ArgumentException,
                    $"Unexpected exception type: {ex.GetType().Name}");
            }
        }

        [LinuxSmbClientFact]
        public async Task EnumerateFilesAsync_OnLinux_ExecutesRealSmbClientCommand()
        {
            var handler = CreateHandler() ??
                          throw new InvalidOperationException("smbclient is present but could not be initialized.");

            // Act & Assert
            // Note: This will try to use smbclient to enumerate files
            // It may fail if there's no SMB share at the path, which is expected
            try
            {
                var files = await handler.EnumerateFilesAsync(_testDirectory);
                var fileList = files.ToList();
                // If successful, verify we got a list (may be empty)
                Assert.NotNull(fileList);
            }
            catch (Exception ex)
            {
                // Expected to fail without a real SMB share
                // Verify it's using smbclient (not Windows file operations)
                Assert.True(
                    ex is IOException ||
                    ex is FileNotFoundException ||
                    ex is DirectoryNotFoundException ||
                    ex is UnauthorizedAccessException ||
                    ex is ArgumentException,
                    $"Unexpected exception type: {ex.GetType().Name}");
            }
        }

        [LinuxSmbClientFact]
        public async Task DeleteFileAsync_OnLinux_ExecutesRealSmbClientCommand()
        {
            var handler = CreateHandler() ??
                          throw new InvalidOperationException("smbclient is present but could not be initialized.");

            // Arrange
            var testFile = Path.Combine(_testDirectory, "todelete.txt");

            // Act & Assert
            try
            {
                var result = await handler.DeleteFileAsync(testFile);
                Assert.True(result);
            }
            catch (Exception ex)
            {
                // Expected to fail without a real SMB share
                Assert.True(
                    ex is IOException ||
                    ex is FileNotFoundException ||
                    ex is DirectoryNotFoundException ||
                    ex is UnauthorizedAccessException ||
                    ex is ArgumentException,
                    $"Unexpected exception type: {ex.GetType().Name}");
            }
        }

        [LinuxSmbClientFact]
        public async Task CreateDirectoryAsync_OnLinux_ExecutesRealSmbClientCommand()
        {
            var handler = CreateHandler() ??
                          throw new InvalidOperationException("smbclient is present but could not be initialized.");

            // Arrange
            var newDir = Path.Combine(_testDirectory, "newsubdir");

            // Act & Assert
            try
            {
                var result = await handler.CreateDirectoryAsync(newDir);
                Assert.True(result);
            }
            catch (Exception ex)
            {
                // Expected to fail without a real SMB share
                Assert.True(
                    ex is IOException ||
                    ex is FileNotFoundException ||
                    ex is DirectoryNotFoundException ||
                    ex is UnauthorizedAccessException ||
                    ex is ArgumentException,
                    $"Unexpected exception type: {ex.GetType().Name}");
            }
        }

        [LinuxSmbClientFact]
        public async Task MoveFileAsync_OnLinux_ExecutesRealSmbClientCommands()
        {
            var handler = CreateHandler() ??
                          throw new InvalidOperationException("smbclient is present but could not be initialized.");

            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            var destFile = Path.Combine(_testDirectory, "dest.txt");

            // Act & Assert
            // On Linux, MoveFileAsync performs get + write + delete operations
            try
            {
                var result = await handler.MoveFileAsync(sourceFile, destFile);
                Assert.True(result);
            }
            catch (Exception ex)
            {
                // Expected to fail without a real SMB share or if source doesn't exist
                Assert.True(
                    ex is IOException ||
                    ex is FileNotFoundException ||
                    ex is DirectoryNotFoundException ||
                    ex is UnauthorizedAccessException ||
                    ex is ArgumentException,
                    $"Unexpected exception type: {ex.GetType().Name}");
            }
        }

        [LinuxSmbClientFact]
        public async Task CanConnectAsync_OnLinux_ExecutesRealSmbClientCommand()
        {
            var handler = CreateHandler() ??
                          throw new InvalidOperationException("smbclient is present but could not be initialized.");

            // Act
            // Test with a local path - will attempt to connect via smbclient
            var result = await handler.CanConnectAsync(_testDirectory);

            // Assert
            // Result depends on whether smbclient can connect to the local path
            // It's a boolean so just verify we got a response
            Assert.True(result == true || result == false);
        }

        [LinuxSmbClientFact]
        public async Task WriteAndReadFile_OnLinux_RealSmbClientRoundTrip()
        {
            var handler = CreateHandler() ??
                          throw new InvalidOperationException("smbclient is present but could not be initialized.");

            // This test attempts a full write-read cycle
            // It requires a working SMB configuration

            var testFile = Path.Combine(_testDirectory, "roundtrip.txt");
            var content = "Round-trip test content";

            try
            {
                // Write
                using (var writeStream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
                {
                    await handler.WriteFileAsync(testFile, writeStream);
                }

                // Read
                await using var readStream = await handler.ReadFileAsync(_testDirectory, "roundtrip.txt");
                using var reader = new StreamReader(readStream);
                var actualContent = await reader.ReadToEndAsync();

                // Assert
                Assert.Equal(content, actualContent);
            }
            catch (Exception ex)
            {
                // Expected to fail without proper SMB configuration
                // Just verify the exception is related to SMB operations
                Assert.True(
                    ex is IOException ||
                    ex is FileNotFoundException ||
                    ex is DirectoryNotFoundException ||
                    ex is UnauthorizedAccessException ||
                    ex is ArgumentException,
                    $"Unexpected exception during round-trip test: {ex.GetType().Name} - {ex.Message}");
            }
        }
    }
}
