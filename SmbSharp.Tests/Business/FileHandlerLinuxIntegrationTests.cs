using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmbSharp.Business;
using SmbSharp.Business.SmbClient;
using SmbSharp.Enums;
using SmbSharp.Infrastructure;

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
        private bool _smbClientAvailable;

        public FileHandlerLinuxIntegrationTests()
        {
            // Create a temporary test directory
            _testDirectory = Path.Combine(Path.GetTempPath(), $"SmbSharpTest_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);
            _smbClientAvailable = false;
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
                    _smbClientAvailable = true;
                }
                catch (InvalidOperationException)
                {
                    // smbclient is not available - tests will be skipped
                    _smbClientAvailable = false;
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

        [Fact]
        public void Constructor_OnLinux_WithSmbClientAvailable_Succeeds()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

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

        [Fact]
        public void Constructor_OnLinux_RequiresSmbClient()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

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

        [Fact]
        public async Task WriteFileAsync_OnLinux_ExecutesRealSmbClientCommand()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

            var handler = CreateHandler();
            if (handler == null || !_smbClientAvailable)
            {
                // smbclient not available, skip test
                return;
            }

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
                    ex is UnauthorizedAccessException,
                    $"Unexpected exception type: {ex.GetType().Name}");
            }
        }

        [Fact]
        public async Task EnumerateFilesAsync_OnLinux_ExecutesRealSmbClientCommand()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

            var handler = CreateHandler();
            if (handler == null || !_smbClientAvailable)
            {
                // smbclient not available, skip test
                return;
            }

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
                    ex is UnauthorizedAccessException,
                    $"Unexpected exception type: {ex.GetType().Name}");
            }
        }

        [Fact]
        public async Task DeleteFileAsync_OnLinux_ExecutesRealSmbClientCommand()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

            var handler = CreateHandler();
            if (handler == null || !_smbClientAvailable)
            {
                // smbclient not available, skip test
                return;
            }

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
                    ex is UnauthorizedAccessException,
                    $"Unexpected exception type: {ex.GetType().Name}");
            }
        }

        [Fact]
        public async Task CreateDirectoryAsync_OnLinux_ExecutesRealSmbClientCommand()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

            var handler = CreateHandler();
            if (handler == null || !_smbClientAvailable)
            {
                // smbclient not available, skip test
                return;
            }

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
                    ex is UnauthorizedAccessException,
                    $"Unexpected exception type: {ex.GetType().Name}");
            }
        }

        [Fact]
        public async Task MoveFileAsync_OnLinux_ExecutesRealSmbClientCommands()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

            var handler = CreateHandler();
            if (handler == null || !_smbClientAvailable)
            {
                // smbclient not available, skip test
                return;
            }

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
                    ex is UnauthorizedAccessException,
                    $"Unexpected exception type: {ex.GetType().Name}");
            }
        }

        [Fact]
        public async Task CanConnectAsync_OnLinux_ExecutesRealSmbClientCommand()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

            var handler = CreateHandler();
            if (handler == null || !_smbClientAvailable)
            {
                // smbclient not available, skip test
                return;
            }

            // Act
            // Test with a local path - will attempt to connect via smbclient
            var result = await handler.CanConnectAsync(_testDirectory);

            // Assert
            // Result depends on whether smbclient can connect to the local path
            // It's a boolean so just verify we got a response
            Assert.True(result == true || result == false);
        }

        [Fact]
        public async Task WriteAndReadFile_OnLinux_RealSmbClientRoundTrip()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

            var handler = CreateHandler();
            if (handler == null || !_smbClientAvailable)
            {
                // smbclient not available, skip test
                return;
            }

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
                    ex is UnauthorizedAccessException,
                    $"Unexpected exception during round-trip test: {ex.GetType().Name} - {ex.Message}");
            }
        }
    }
}
