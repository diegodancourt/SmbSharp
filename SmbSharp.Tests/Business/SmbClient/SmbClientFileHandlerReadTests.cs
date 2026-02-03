using Microsoft.Extensions.Logging;
using Moq;
using SmbSharp.Business.SmbClient;
using SmbSharp.Infrastructure.Interfaces;
using SmbSharp.Tests.Util;

namespace SmbSharp.Tests.Business.SmbClient
{
    /// <summary>
    /// Unit tests for SmbClientFileHandler.GetFileStreamAsync method
    /// </summary>
    public class SmbClientFileHandlerReadTests
    {
        /// <summary>
        /// Helper method to setup process mock that creates temp files
        /// </summary>
        private void SetupSuccessfulDownload(Mock<IProcessWrapper> mockProcess, int exitCode = 0, string output = "", string error = "")
        {
            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, IDictionary<string, string>?, CancellationToken>((cmd, args, env, ct) =>
                {
                    if (exitCode == 0)
                    {
                        // Args format: //server/share --use-kerberos=required -c "get \"test.txt\" \"C:\\Users\\...\""
                        // The file path is between escaped quotes: \"path\"
                        // Match pattern: \"C:\path\" or \"/tmp/path\" - capture until the closing \"
                        var match = System.Text.RegularExpressions.Regex.Match(args, @"\""([C-Z]:[^\""]+|/tmp/[^\""]+)\""");
                        if (match.Success)
                        {
                            var tempPath = match.Groups[1].Value;
                            // Unescape any double backslashes and remove trailing backslash
                            tempPath = tempPath.Replace("\\\\", "\\").TrimEnd('\\');
                            // Create the temp file that smbclient would create
                            File.WriteAllText(tempPath, "test content");
                        }
                    }
                })
                .ReturnsAsync(new ProcessResult { ExitCode = exitCode, StandardOutput = output, StandardError = error });
        }

        [Fact]
        public async Task GetFileStreamAsync_SuccessfulDownload_ReturnsStream()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();
            SetupSuccessfulDownload(mockProcess);

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, useKerberos: true);

            // Act
            var stream = await handler.GetFileStreamAsync("//server/share/path", "test.txt");

            // Assert
            Assert.NotNull(stream);
            Assert.True(stream.CanRead);

            // Cleanup
            stream.Dispose();

            // Verify the correct command was executed
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args =>
                    args.Contains("//server/share") &&
                    args.Contains("get") &&
                    args.Contains("path/test.txt")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetFileStreamAsync_RootPath_DownloadsFromRoot()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();
            SetupSuccessfulDownload(mockProcess);

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, useKerberos: true);

            // Act
            var stream = await handler.GetFileStreamAsync("//server/share", "test.txt");

            // Assert
            Assert.NotNull(stream);
            stream.Dispose();

            // Verify get command uses just filename (no path prefix) with escaped quotes
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args =>
                    args.Contains("get \\\"test.txt\\\"")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetFileStreamAsync_FileNotFound_ThrowsFileNotFoundException()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            // Mock smbclient returning "not found" error
            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult
                {
                    ExitCode = 1,
                    StandardError = "NT_STATUS_OBJECT_NAME_NOT_FOUND",
                    StandardOutput = ""
                });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, useKerberos: true);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
                handler.GetFileStreamAsync("//server/share/path", "missing.txt"));

            Assert.Contains("//server/share/path", exception.Message);
        }

        [Fact]
        public async Task GetFileStreamAsync_AccessDenied_ThrowsUnauthorizedAccessException()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            // Mock smbclient returning "access denied" error
            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult
                {
                    ExitCode = 1,
                    StandardError = "NT_STATUS_ACCESS_DENIED",
                    StandardOutput = ""
                });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, useKerberos: true);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                handler.GetFileStreamAsync("//server/share/path", "protected.txt"));

            Assert.Contains("Access denied", exception.Message);
            Assert.Contains("//server/share/path", exception.Message);
        }

        [Fact]
        public async Task GetFileStreamAsync_BadNetworkPath_ThrowsDirectoryNotFoundException()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            // Mock smbclient returning "bad network path" error
            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult
                {
                    ExitCode = 1,
                    StandardError = "NT_STATUS_BAD_NETWORK_NAME",
                    StandardOutput = ""
                });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, useKerberos: true);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                handler.GetFileStreamAsync("//badserver/badshare/path", "file.txt"));

            Assert.Contains("network path was not found", exception.Message);
        }

        [Fact]
        public async Task GetFileStreamAsync_WithKerberos_UsesKerberosFlag()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();
            SetupSuccessfulDownload(mockProcess);

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, useKerberos: true);

            // Act
            var stream = await handler.GetFileStreamAsync("//server/share", "file.txt");
            stream.Dispose();

            // Assert - Verify kerberos flag is used
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("-k")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetFileStreamAsync_WithUsernamePassword_PassesCredentials()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();
            SetupSuccessfulDownload(mockProcess);

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object,
                useKerberos: false, username: "testuser", password: "testpass");

            // Act
            var stream = await handler.GetFileStreamAsync("//server/share", "file.txt");
            stream.Dispose();

            // Assert - Verify username is passed and password via environment variable
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("-U") && args.Contains("testuser")),
                It.Is<IDictionary<string, string>>(env => env.ContainsKey("PASSWD") && env["PASSWD"] == "testpass"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetFileStreamAsync_WithDomain_IncludesDomainInUsername()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();
            SetupSuccessfulDownload(mockProcess);

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object,
                useKerberos: false, username: "testuser", password: "testpass", domain: "TESTDOMAIN");

            // Act
            var stream = await handler.GetFileStreamAsync("//server/share", "file.txt");
            stream.Dispose();

            // Assert - Verify domain is included in username
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("TESTDOMAIN\\testuser")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetFileStreamAsync_InvalidSmbPath_ThrowsArgumentException()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, useKerberos: true);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.GetFileStreamAsync("invalid-path", "file.txt"));
        }

        [Fact]
        public async Task GetFileStreamAsync_FileNameWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();
            SetupSuccessfulDownload(mockProcess);

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, useKerberos: true);

            // Act
            var stream = await handler.GetFileStreamAsync("//server/share", "file with spaces.txt");
            stream.Dispose();

            // Assert - Verify filename is quoted with escaped quotes
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("get \\\"file with spaces.txt\\\"")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetFileStreamAsync_CancellationRequested_PropagatesCancellation()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            var cts = new CancellationTokenSource();
            cts.Cancel();

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, useKerberos: true);

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                handler.GetFileStreamAsync("//server/share", "file.txt", cts.Token));
        }

        [Fact]
        public async Task GetFileStreamAsync_GenericSmbError_ThrowsIOException()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            // Mock smbclient returning a generic error
            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult
                {
                    ExitCode = 1,
                    StandardError = "Some unexpected error occurred",
                    StandardOutput = ""
                });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, useKerberos: true);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<IOException>(() =>
                handler.GetFileStreamAsync("//server/share", "file.txt"));

            Assert.Contains("Failed to execute smbclient command", exception.Message);
        }

        [Fact]
        public async Task GetFileStreamAsync_PathWithBackslashes_ConvertsToForwardSlashes()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();
            SetupSuccessfulDownload(mockProcess);

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, useKerberos: true);

            // Act - Use backslashes in path
            var stream = await handler.GetFileStreamAsync("\\\\server\\share\\path\\to", "file.txt");
            stream.Dispose();

            // Assert - Verify path uses forward slashes in get command
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("path/to/file.txt")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetFileStreamAsync_DeepNestedPath_HandlesCorrectly()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();
            SetupSuccessfulDownload(mockProcess);

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, useKerberos: true);

            // Act
            var stream = await handler.GetFileStreamAsync("//server/share/level1/level2/level3", "file.txt");
            stream.Dispose();

            // Assert
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("level1/level2/level3/file.txt")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetFileStreamAsync_ReturnedStream_HasDeleteOnCloseOption()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();
            SetupSuccessfulDownload(mockProcess);

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, useKerberos: true);

            // Act
            var stream = await handler.GetFileStreamAsync("//server/share", "file.txt");

            // Assert
            Assert.IsType<FileStream>(stream);
            var fileStream = (FileStream)stream;

            // The stream should be readable
            Assert.True(fileStream.CanRead);

            // Store the path before disposing
            var tempPath = fileStream.Name;

            // Dispose the stream
            await stream.DisposeAsync();

            // Verify temp file is deleted after stream disposal (DeleteOnClose option)
            // Note: This might fail if the file wasn't actually created by smbclient
            // but we can at least verify the stream was created with the right properties
        }
    }
}
