using Microsoft.Extensions.Logging;
using Moq;
using SmbSharp.Business;
using SmbSharp.Business.Interfaces;
using SmbSharp.Tests.Util;

namespace SmbSharp.Tests.Business
{
    /// <summary>
    /// Unit tests for FileHandler constructor and initialization
    /// </summary>
    public class FileHandlerConstructorTests
    {
        [Fact]
        public void Constructor_WithValidDependencies_ShouldSucceed()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<FileHandler>>();
            var mockSmbClient = new Mock<ISmbClientFileHandler>();
            mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);

            // Act & Assert - Should not throw on supported platforms
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                var handler = new FileHandler(mockLogger.Object, mockSmbClient.Object);
                Assert.NotNull(handler);
            }
        }

        [Fact]
        public void Constructor_OnLinux_SmbClientNotAvailable_ShouldThrowInvalidOperationException()
        {
            // This test only runs on Linux where we can control smbclient availability
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux platforms
            }

            // Arrange
            var mockLogger = new Mock<ILogger<FileHandler>>();
            var mockSmbClient = new Mock<ISmbClientFileHandler>();

            // Mock smbclient as not available
            mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(false);

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new FileHandler(mockLogger.Object, mockSmbClient.Object));

            Assert.Contains("smbclient is not installed", exception.Message);
            Assert.Contains("apt-get install smbclient", exception.Message);

            // Verify error was logged
            mockLogger.VerifyLog(LogLevel.Error, "smbclient is not installed or not available in PATH");
        }

        [Fact]
        public void Constructor_OnUnsupportedPlatform_ShouldThrowPlatformNotSupportedException()
        {
            // This test verifies that the error handling works correctly
            // On macOS, FreeBSD, or other unsupported platforms, this should throw
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                return; // Skip on supported platforms
            }

            // Arrange
            var mockLogger = new Mock<ILogger<FileHandler>>();
            var mockSmbClient = new Mock<ISmbClientFileHandler>();
            mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);

            // Act & Assert
            var exception = Assert.Throws<PlatformNotSupportedException>(() =>
                new FileHandler(mockLogger.Object, mockSmbClient.Object));

            Assert.Contains("SmbSharp only supports Windows and Linux", exception.Message);

            // Verify error was logged
            mockLogger.VerifyLog(LogLevel.Error, "Unsupported platform");
        }

        [Fact]
        public void Constructor_OnLinux_SmbClientAvailable_ShouldSucceed()
        {
            // This test verifies Linux-specific initialization
            if (!OperatingSystem.IsLinux())
            {
                return; // Skip on non-Linux platforms
            }

            // Arrange
            var mockLogger = new Mock<ILogger<FileHandler>>();
            var mockSmbClient = new Mock<ISmbClientFileHandler>();

            // Mock smbclient as available
            mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);

            // Act
            var handler = new FileHandler(mockLogger.Object, mockSmbClient.Object);

            // Assert
            Assert.NotNull(handler);
            mockSmbClient.Verify(x => x.IsSmbClientAvailable(), Times.Once);
        }

        [Fact]
        public void Constructor_OnWindows_DoesNotCheckSmbClient()
        {
            // This test verifies that on Windows, smbclient availability is not checked
            if (!OperatingSystem.IsWindows())
            {
                return; // Skip on non-Windows platforms
            }

            // Arrange
            var mockLogger = new Mock<ILogger<FileHandler>>();
            var mockSmbClient = new Mock<ISmbClientFileHandler>();

            // Don't set up IsSmbClientAvailable - if it's called, the test will fail

            // Act
            var handler = new FileHandler(mockLogger.Object, mockSmbClient.Object);

            // Assert
            Assert.NotNull(handler);
            // Verify smbclient availability was NOT checked on Windows
            mockSmbClient.Verify(x => x.IsSmbClientAvailable(), Times.Never);
        }
    }

    /// <summary>
    /// Unit tests for FileHandler.EnumerateFilesAsync
    /// </summary>
    public class FileHandlerEnumerateFilesTests
    {
        private FileHandler CreateHandler()
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                var mockLogger = new Mock<ILogger<FileHandler>>();
                var mockSmbClient = new Mock<ISmbClientFileHandler>();
                mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);
                return new FileHandler(mockLogger.Object, mockSmbClient.Object);
            }
            throw new PlatformNotSupportedException("Tests can only run on Windows or Linux");
        }

        [Fact]
        public async Task EnumerateFilesAsync_NullDirectory_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.EnumerateFilesAsync(null!));

            Assert.Equal("directory", exception.ParamName);
            Assert.Contains("Directory path cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task EnumerateFilesAsync_EmptyDirectory_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.EnumerateFilesAsync(""));

            Assert.Equal("directory", exception.ParamName);
            Assert.Contains("Directory path cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task EnumerateFilesAsync_WhitespaceDirectory_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.EnumerateFilesAsync("   "));

            Assert.Equal("directory", exception.ParamName);
            Assert.Contains("Directory path cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task EnumerateFilesAsync_WithCancellationToken_ShouldRespectCancellation()
        {
            // Arrange
            var handler = CreateHandler();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                handler.EnumerateFilesAsync("//nonexistent/share", cts.Token));
        }
    }

    /// <summary>
    /// Unit tests for FileHandler.ReadFileAsync
    /// </summary>
    public class FileHandlerReadFileTests
    {
        private FileHandler CreateHandler()
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                var mockLogger = new Mock<ILogger<FileHandler>>();
                var mockSmbClient = new Mock<ISmbClientFileHandler>();
                mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);
                return new FileHandler(mockLogger.Object, mockSmbClient.Object);
            }
            throw new PlatformNotSupportedException("Tests can only run on Windows or Linux");
        }

        [Fact]
        public async Task ReadFileAsync_NullDirectory_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.ReadFileAsync(null!, "file.txt"));

            Assert.Equal("directory", exception.ParamName);
            Assert.Contains("Directory path cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task ReadFileAsync_EmptyDirectory_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.ReadFileAsync("", "file.txt"));

            Assert.Equal("directory", exception.ParamName);
            Assert.Contains("Directory path cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task ReadFileAsync_NullFileName_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.ReadFileAsync("//server/share", null!));

            Assert.Equal("fileName", exception.ParamName);
            Assert.Contains("File name cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task ReadFileAsync_EmptyFileName_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.ReadFileAsync("//server/share", ""));

            Assert.Equal("fileName", exception.ParamName);
            Assert.Contains("File name cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task ReadFileAsync_WithCancellationToken_ShouldRespectCancellation()
        {
            // Arrange
            var handler = CreateHandler();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                handler.ReadFileAsync("//nonexistent/share", "file.txt", cts.Token));
        }
    }

    /// <summary>
    /// Unit tests for FileHandler.WriteFileAsync
    /// </summary>
    public class FileHandlerWriteFileTests
    {
        private FileHandler CreateHandler()
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                var mockLogger = new Mock<ILogger<FileHandler>>();
                var mockSmbClient = new Mock<ISmbClientFileHandler>();
                mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);
                return new FileHandler(mockLogger.Object, mockSmbClient.Object);
            }
            throw new PlatformNotSupportedException("Tests can only run on Windows or Linux");
        }

        [Fact]
        public async Task WriteFileAsync_String_NullFilePath_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.WriteFileAsync(null!, "content"));

            Assert.Equal("filePath", exception.ParamName);
            Assert.Contains("File path cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task WriteFileAsync_String_EmptyFilePath_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.WriteFileAsync("", "content"));

            Assert.Equal("filePath", exception.ParamName);
            Assert.Contains("File path cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task WriteFileAsync_String_NullContent_ShouldThrowArgumentNullException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                handler.WriteFileAsync("//server/share/file.txt", (string)null!));

            Assert.Equal("content", exception.ParamName);
        }

        [Fact]
        public async Task WriteFileAsync_Stream_NullFilePath_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();
            using var stream = new MemoryStream();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.WriteFileAsync(null!, stream));

            Assert.Equal("filePath", exception.ParamName);
            Assert.Contains("File path cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task WriteFileAsync_Stream_EmptyFilePath_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();
            using var stream = new MemoryStream();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.WriteFileAsync("", stream));

            Assert.Equal("filePath", exception.ParamName);
            Assert.Contains("File path cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task WriteFileAsync_Stream_NullStream_ShouldThrowArgumentNullException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                handler.WriteFileAsync("//server/share/file.txt", (Stream)null!));

            Assert.Equal("stream", exception.ParamName);
        }

        [Fact]
        public async Task WriteFileAsync_Stream_ResetsStreamPosition_WhenSeekable()
        {
            // Arrange
            var handler = CreateHandler();
            using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
            stream.Position = 3; // Move position away from start

            // This test verifies the stream position reset logic
            // We can't fully test the write without a real SMB share
            // but we can verify the ArgumentException is thrown for path validation
            // after the stream position would have been reset

            // Act & Assert - Will fail on path parsing, but that's after position reset
            try
            {
                await handler.WriteFileAsync("invalid-path", stream);
            }
            catch
            {
                // Expected to fail, but stream position should have been reset
                Assert.Equal(0, stream.Position);
            }
        }

        [Fact]
        public async Task WriteFileAsync_WithCancellationToken_ShouldRespectCancellation()
        {
            // Arrange
            var handler = CreateHandler();
            using var stream = new MemoryStream();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                handler.WriteFileAsync("//nonexistent/share/file.txt", stream, cts.Token));
        }
    }

    /// <summary>
    /// Unit tests for FileHandler.DeleteFileAsync
    /// </summary>
    public class FileHandlerDeleteFileTests
    {
        private FileHandler CreateHandler()
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                var mockLogger = new Mock<ILogger<FileHandler>>();
                var mockSmbClient = new Mock<ISmbClientFileHandler>();
                mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);
                return new FileHandler(mockLogger.Object, mockSmbClient.Object);
            }
            throw new PlatformNotSupportedException("Tests can only run on Windows or Linux");
        }

        [Fact]
        public async Task DeleteFileAsync_NullFilePath_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.DeleteFileAsync(null!));

            Assert.Equal("filePath", exception.ParamName);
            Assert.Contains("File path cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task DeleteFileAsync_EmptyFilePath_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.DeleteFileAsync(""));

            Assert.Equal("filePath", exception.ParamName);
            Assert.Contains("File path cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task DeleteFileAsync_WithCancellationToken_ShouldRespectCancellation()
        {
            // Arrange
            var handler = CreateHandler();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                handler.DeleteFileAsync("//nonexistent/share/file.txt", cts.Token));
        }
    }

    /// <summary>
    /// Unit tests for FileHandler.MoveFileAsync
    /// </summary>
    public class FileHandlerMoveFileTests
    {
        private FileHandler CreateHandler()
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                var mockLogger = new Mock<ILogger<FileHandler>>();
                var mockSmbClient = new Mock<ISmbClientFileHandler>();
                mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);
                return new FileHandler(mockLogger.Object, mockSmbClient.Object);
            }
            throw new PlatformNotSupportedException("Tests can only run on Windows or Linux");
        }

        [Fact]
        public async Task MoveFileAsync_NullSourcePath_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.MoveFileAsync(null!, "//server/share/dest.txt"));

            Assert.Equal("sourceFilePath", exception.ParamName);
            Assert.Contains("Source file path cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task MoveFileAsync_EmptySourcePath_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.MoveFileAsync("", "//server/share/dest.txt"));

            Assert.Equal("sourceFilePath", exception.ParamName);
            Assert.Contains("Source file path cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task MoveFileAsync_NullDestPath_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.MoveFileAsync("//server/share/source.txt", null!));

            Assert.Equal("destinationFilePath", exception.ParamName);
            Assert.Contains("Destination file path cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task MoveFileAsync_EmptyDestPath_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.MoveFileAsync("//server/share/source.txt", ""));

            Assert.Equal("destinationFilePath", exception.ParamName);
            Assert.Contains("Destination file path cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task MoveFileAsync_WithCancellationToken_ShouldRespectCancellation()
        {
            // Arrange
            var handler = CreateHandler();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                handler.MoveFileAsync(
                    "//nonexistent/share/source.txt",
                    "//nonexistent/share/dest.txt",
                    cts.Token));
        }
    }

    /// <summary>
    /// Unit tests for FileHandler.CreateDirectoryAsync
    /// </summary>
    public class FileHandlerCreateDirectoryTests
    {
        private FileHandler CreateHandler()
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                var mockLogger = new Mock<ILogger<FileHandler>>();
                var mockSmbClient = new Mock<ISmbClientFileHandler>();
                mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);
                return new FileHandler(mockLogger.Object, mockSmbClient.Object);
            }
            throw new PlatformNotSupportedException("Tests can only run on Windows or Linux");
        }

        [Fact]
        public async Task CreateDirectoryAsync_NullPath_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.CreateDirectoryAsync(null!));

            Assert.Equal("directoryPath", exception.ParamName);
            Assert.Contains("Directory path cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task CreateDirectoryAsync_EmptyPath_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.CreateDirectoryAsync(""));

            Assert.Equal("directoryPath", exception.ParamName);
            Assert.Contains("Directory path cannot be null or empty", exception.Message);
        }

        [Fact]
        public async Task CreateDirectoryAsync_WithCancellationToken_ShouldRespectCancellation()
        {
            // Arrange
            var handler = CreateHandler();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                handler.CreateDirectoryAsync("//nonexistent/share/newdir", cts.Token));
        }
    }

}
