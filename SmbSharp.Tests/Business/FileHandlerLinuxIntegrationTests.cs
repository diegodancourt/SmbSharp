using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using SmbSharp.Business;
using SmbSharp.Business.Interfaces;
using SmbSharp.Enums;

namespace SmbSharp.Tests.Business
{
    /// <summary>
    /// Integration tests for FileHandler on Linux using smbclient.
    /// These tests verify the Linux code path and require smbclient to be installed.
    /// Tests using local temp directory to verify handler initialization and basic operations.
    /// For full SMB testing, a real SMB share is required (manual/CI testing).
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

        private FileHandler CreateHandler()
        {
            if (!OperatingSystem.IsLinux())
            {
                // Skip handler creation on non-Linux
                return null!;
            }

            if (_handler == null)
            {
                var mockLogger = new Mock<ILogger<FileHandler>>();
                var mockSmbClient = new Mock<ISmbClientFileHandler>();
                mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);
                _handler = new FileHandler(mockLogger.Object, mockSmbClient.Object);
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
            var handler = CreateHandler();

            // Assert
            Assert.NotNull(handler);
        }

        [Fact]
        public void Constructor_OnLinux_VerifiesSmbClientAvailability()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

            // Arrange
            var mockLogger = new Mock<ILogger<FileHandler>>();
            var mockSmbClient = new Mock<ISmbClientFileHandler>();
            mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);

            // Act
            var handler = new FileHandler(mockLogger.Object, mockSmbClient.Object);

            // Assert
            Assert.NotNull(handler);
            mockSmbClient.Verify(x => x.IsSmbClientAvailable(), Times.Once);
        }

        [Fact]
        public void Constructor_OnLinux_SmbClientNotAvailable_ThrowsInvalidOperationException()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

            // Arrange
            var mockLogger = new Mock<ILogger<FileHandler>>();
            var mockSmbClient = new Mock<ISmbClientFileHandler>();
            mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(false);

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new FileHandler(mockLogger.Object, mockSmbClient.Object));

            Assert.Contains("smbclient is not installed", exception.Message);
        }

        [Fact]
        public async Task WriteFileAsync_OnLinux_UsesSmbClientFileHandler()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

            // Arrange
            var mockLogger = new Mock<ILogger<FileHandler>>();
            var mockSmbClient = new Mock<ISmbClientFileHandler>();
            mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);
            mockSmbClient
                .Setup(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                    It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var handler = new FileHandler(mockLogger.Object, mockSmbClient.Object);
            var testFile = Path.Combine(_testDirectory, "test.txt");
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));

            // Act
            var result = await handler.WriteFileAsync(testFile, stream);

            // Assert
            Assert.True(result);
            mockSmbClient.Verify(x => x.WriteFileAsync(
                _testDirectory,
                "test.txt",
                It.IsAny<Stream>(),
                FileWriteMode.Overwrite,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ReadFileAsync_OnLinux_UsesSmbClientFileHandler()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

            // Arrange
            var mockLogger = new Mock<ILogger<FileHandler>>();
            var mockSmbClient = new Mock<ISmbClientFileHandler>();
            mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);
            mockSmbClient
                .Setup(x => x.GetFileStreamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("file content")));

            var handler = new FileHandler(mockLogger.Object, mockSmbClient.Object);

            // Act
            await using var stream = await handler.ReadFileAsync(_testDirectory, "test.txt");

            // Assert
            Assert.NotNull(stream);
            mockSmbClient.Verify(x => x.GetFileStreamAsync(
                _testDirectory,
                "test.txt",
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteFileAsync_OnLinux_UsesSmbClientFileHandler()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

            // Arrange
            var mockLogger = new Mock<ILogger<FileHandler>>();
            var mockSmbClient = new Mock<ISmbClientFileHandler>();
            mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);
            mockSmbClient
                .Setup(x => x.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var handler = new FileHandler(mockLogger.Object, mockSmbClient.Object);
            var testFile = Path.Combine(_testDirectory, "test.txt");

            // Act
            var result = await handler.DeleteFileAsync(testFile);

            // Assert
            Assert.True(result);
            mockSmbClient.Verify(x => x.DeleteFileAsync(
                _testDirectory,
                "test.txt",
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EnumerateFilesAsync_OnLinux_UsesSmbClientFileHandler()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

            // Arrange
            var mockLogger = new Mock<ILogger<FileHandler>>();
            var mockSmbClient = new Mock<ISmbClientFileHandler>();
            mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);
            mockSmbClient
                .Setup(x => x.EnumerateFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { "file1.txt", "file2.txt", "file3.doc" });

            var handler = new FileHandler(mockLogger.Object, mockSmbClient.Object);

            // Act
            var files = await handler.EnumerateFilesAsync(_testDirectory);
            var fileList = files.ToList();

            // Assert
            Assert.Equal(3, fileList.Count);
            Assert.Contains("file1.txt", fileList);
            Assert.Contains("file2.txt", fileList);
            Assert.Contains("file3.doc", fileList);
            mockSmbClient.Verify(x => x.EnumerateFilesAsync(
                _testDirectory,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CreateDirectoryAsync_OnLinux_UsesSmbClientFileHandler()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

            // Arrange
            var mockLogger = new Mock<ILogger<FileHandler>>();
            var mockSmbClient = new Mock<ISmbClientFileHandler>();
            mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);
            mockSmbClient
                .Setup(x => x.CreateDirectoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var handler = new FileHandler(mockLogger.Object, mockSmbClient.Object);
            var newDir = Path.Combine(_testDirectory, "subdir");

            // Act
            var result = await handler.CreateDirectoryAsync(newDir);

            // Assert
            Assert.True(result);
            mockSmbClient.Verify(x => x.CreateDirectoryAsync(
                newDir,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task MoveFileAsync_OnLinux_UsesSmbClientFileHandler()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

            // Arrange
            var mockLogger = new Mock<ILogger<FileHandler>>();
            var mockSmbClient = new Mock<ISmbClientFileHandler>();
            mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);

            // Mock the get, write, and delete operations that MoveFileAsync performs on Linux
            mockSmbClient
                .Setup(x => x.GetFileStreamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("file content")));
            mockSmbClient
                .Setup(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            mockSmbClient
                .Setup(x => x.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var handler = new FileHandler(mockLogger.Object, mockSmbClient.Object);
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            var destFile = Path.Combine(_testDirectory, "dest.txt");

            // Act
            var result = await handler.MoveFileAsync(sourceFile, destFile);

            // Assert
            Assert.True(result);
            // Verify it performed get (read), write (to dest), and delete (source)
            mockSmbClient.Verify(x => x.GetFileStreamAsync(
                It.IsAny<string>(),
                "source.txt",
                It.IsAny<CancellationToken>()), Times.Once);
            mockSmbClient.Verify(x => x.WriteFileAsync(
                It.IsAny<string>(),
                "dest.txt",
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()), Times.Once);
            mockSmbClient.Verify(x => x.DeleteFileAsync(
                It.IsAny<string>(),
                "source.txt",
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CanConnectAsync_OnLinux_UsesSmbClientFileHandler()
        {
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux
            }

            // Arrange
            var mockLogger = new Mock<ILogger<FileHandler>>();
            var mockSmbClient = new Mock<ISmbClientFileHandler>();
            mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);
            mockSmbClient
                .Setup(x => x.CanConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var handler = new FileHandler(mockLogger.Object, mockSmbClient.Object);

            // Act
            var result = await handler.CanConnectAsync("//server/share");

            // Assert
            Assert.True(result);
            mockSmbClient.Verify(x => x.CanConnectAsync(
                "//server/share",
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}