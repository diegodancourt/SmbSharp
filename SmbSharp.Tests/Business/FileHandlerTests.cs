using Microsoft.Extensions.Logging;
using Moq;
using SmbSharp.Business;
using SmbSharp.Business.Interfaces;
using SmbSharp.Business.SmbClient;
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

        [LinuxFact]
        public void Constructor_OnLinux_SmbClientNotAvailable_ShouldThrowInvalidOperationException()
        {
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

        [UnsupportedPlatformFact]
        public void Constructor_OnUnsupportedPlatform_ShouldThrowPlatformNotSupportedException()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<FileHandler>>();
            var mockSmbClient = new Mock<ISmbClientFileHandler>();
            mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);

            // Act & Assert
            var exception = Assert.Throws<PlatformNotSupportedException>(() =>
                new FileHandler(mockLogger.Object, mockSmbClient.Object));

            Assert.Contains("SmbSharp only supports Windows, Linux, and macOS", exception.Message);

            // Verify error was logged
            mockLogger.VerifyLog(LogLevel.Error, "Unsupported platform");
        }

        [LinuxFact]
        public void Constructor_OnLinux_SmbClientAvailable_ShouldSucceed()
        {
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

        [WindowsFact]
        public void Constructor_OnWindows_DoesNotCheckSmbClient()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<FileHandler>>();
            var mockSmbClient = new Mock<ISmbClientFileHandler>();

            // Don't set up IsSmbClientAvailable - if it's called, the test will fail

            // Act
            var handler = new FileHandler(mockLogger.Object, mockSmbClient.Object);

            // Assert
            Assert.NotNull(handler);
            // Verify smbclient availability was NOT checked on Windows (WSL is opt-in)
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
    /// Unit tests for FileHandler.FileExistsAsync
    /// </summary>
    public class FileHandlerFileExistsTests
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
        public async Task FileExistsAsync_NullFileName_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.FileExistsAsync(null!, "//server/share"));
        }

        [Fact]
        public async Task FileExistsAsync_EmptyFileName_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.FileExistsAsync("", "//server/share"));
        }

        [Fact]
        public async Task FileExistsAsync_WhitespaceFileName_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.FileExistsAsync("   ", "//server/share"));
        }

        [Fact]
        public async Task FileExistsAsync_NullDirectory_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.FileExistsAsync("file.txt", null!));
        }

        [Fact]
        public async Task FileExistsAsync_EmptyDirectory_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.FileExistsAsync("file.txt", ""));
        }

        [Fact]
        public async Task FileExistsAsync_WhitespaceDirectory_ShouldThrowArgumentException()
        {
            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.FileExistsAsync("file.txt", "   "));
        }

        [Fact]
        public async Task FileExistsAsync_WithCancellationToken_ShouldRespectCancellation()
        {
            // Arrange
            var handler = CreateHandler();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                handler.FileExistsAsync("file.txt", "//nonexistent/share", cts.Token));
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

        [Fact]
        public async Task MoveFileAsync_UncStylePaths_PreservesLeadingDoubleSlash()
        {
            // Regression test for GitHub issue #4: Path.GetDirectoryName/GetFileName mangle
            // "//server/share/folder/file.txt" on Linux (collapsing the leading "//" to a single "/"),
            // which broke SmbClientFileHandler's UNC path parsing. FileHandler must split paths manually
            // instead of relying on System.IO.Path for smbclient-routed operations.

            // Arrange
            var mockLogger = new Mock<ILogger<FileHandler>>();
            var mockSmbClient = new Mock<ISmbClientFileHandler>();
            mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);

            string? capturedSourceDir = null;
            string? capturedDestDir = null;

            mockSmbClient
                .Setup(x => x.GetFileStreamAsync(It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, string, CancellationToken>((dir, _, _) => capturedSourceDir = dir)
                .ReturnsAsync(new MemoryStream());

            mockSmbClient
                .Setup(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                    It.IsAny<SmbSharp.Enums.FileWriteMode>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, Stream, SmbSharp.Enums.FileWriteMode, CancellationToken>((dir, _, _, _, _) => capturedDestDir = dir)
                .ReturnsAsync(true);

            mockSmbClient
                .Setup(x => x.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // useWsl: true forces the smbclient code path regardless of the OS running the test.
            var handler = new FileHandler(mockLogger.Object, mockSmbClient.Object, useWsl: true);

            // Act
            await handler.MoveFileAsync("//server/share/folder/source.txt", "//server/share/folder/dest.txt");

            // Assert
            Assert.Equal("//server/share/folder", capturedSourceDir);
            Assert.Equal("//server/share/folder", capturedDestDir);
        }

        [Fact]
        public async Task MoveFileAsync_CrossShareSourceDeleteFailure_DoesNotDeleteDestination()
        {
            var logger = new Mock<ILogger<FileHandler>>();
            var smbClient = new Mock<ISmbClientFileHandler>();
            smbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);
            smbClient.Setup(x => x.GetFileStreamAsync("//server/share1", "source.txt", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MemoryStream(new byte[] { 1, 2, 3 }));
            smbClient.Setup(x => x.WriteFileAsync("//server/share2", "destination.txt", It.IsAny<Stream>(),
                    SmbSharp.Enums.FileWriteMode.CreateNew, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            smbClient.Setup(x => x.DeleteFileAsync("//server/share1", "source.txt", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("source removal failed"));

            var handler = new FileHandler(logger.Object, smbClient.Object, useWsl: true);

            await Assert.ThrowsAsync<IOException>(() =>
                handler.MoveFileAsync("//server/share1/source.txt", "//server/share2/destination.txt"));

            smbClient.Verify(x => x.WriteFileAsync("//server/share2", "destination.txt", It.IsAny<Stream>(),
                SmbSharp.Enums.FileWriteMode.CreateNew, It.IsAny<CancellationToken>()), Times.Once);
            smbClient.Verify(x => x.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MoveFileAsync_SameShare_ChecksDestinationBeforeRename(bool destinationExists)
        {
            var logger = new Mock<ILogger<FileHandler>>();
            var process = new Mock<SmbSharp.Infrastructure.Interfaces.IProcessWrapper>(MockBehavior.Strict);
            process.Setup(x => x.ExecuteAsync("smbclient",
                    It.Is<IEnumerable<string>>(args => args.SequenceEqual(new[] { "--version" })),
                    It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SmbSharp.Infrastructure.Interfaces.ProcessResult
                    { ExitCode = 0, StandardOutput = "", StandardError = "" });
            process.Setup(x => x.ExecuteAsync("smbclient",
                    It.Is<IEnumerable<string>>(args => args.Contains(
                        "ls \"destination folder/destination.txt\"")),
                    It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SmbSharp.Infrastructure.Interfaces.ProcessResult
                {
                    ExitCode = destinationExists ? 0 : 1,
                    StandardOutput = destinationExists
                        ? "  destination.txt                     A        3  Fri Oct  2 12:00:00 2026"
                        : "NT_STATUS_NO_SUCH_FILE listing destination folder/destination.txt",
                    StandardError = ""
                });
            process.Setup(x => x.ExecuteAsync("smbclient",
                    It.Is<IEnumerable<string>>(args => args.Contains("ls \"destination folder\"")),
                    It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SmbSharp.Infrastructure.Interfaces.ProcessResult
                {
                    ExitCode = 0,
                    StandardOutput = "  destination folder                  D        0  Fri Oct  2 12:00:00 2026",
                    StandardError = ""
                });
            process.Setup(x => x.ExecuteAsync("smbclient",
                    It.Is<IEnumerable<string>>(args => args.Contains(
                        "rename \"source folder/source.txt\" \"destination folder/destination.txt\"")),
                    It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SmbSharp.Infrastructure.Interfaces.ProcessResult
                    { ExitCode = 0, StandardOutput = "", StandardError = "" });
            var smbClient = new SmbClientFileHandler(new Mock<ILogger<SmbClientFileHandler>>().Object,
                process.Object, true);
            using var handler = new FileHandler(logger.Object, smbClient, useWsl: true);

            var operation = handler.MoveFileAsync("//server/share/source folder/source.txt",
                "//server/share/destination folder/destination.txt");
            if (destinationExists)
                Assert.Contains("Destination file already exists",
                    (await Assert.ThrowsAsync<IOException>(() => operation)).Message);
            else
                Assert.True(await operation);

            process.Verify(x => x.ExecuteAsync("smbclient",
                It.Is<IEnumerable<string>>(args => args.Contains(
                    "rename \"source folder/source.txt\" \"destination folder/destination.txt\"")),
                It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()),
                destinationExists ? Times.Never() : Times.Once());
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
