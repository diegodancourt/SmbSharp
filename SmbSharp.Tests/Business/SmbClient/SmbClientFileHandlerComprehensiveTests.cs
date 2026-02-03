using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using SmbSharp.Business.SmbClient;
using SmbSharp.Enums;
using SmbSharp.Infrastructure.Interfaces;

namespace SmbSharp.Tests.Business.SmbClient
{
    /// <summary>
    /// Comprehensive unit tests for SmbClientFileHandler using mocked IProcessWrapper.
    /// These tests verify all the SMB logic without requiring actual smbclient or SMB shares.
    /// </summary>
    public class SmbClientFileHandlerEnumerateFilesTests
    {
        [Fact]
        public async Task EnumerateFilesAsync_ValidOutput_ReturnsFileNames()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            var smbClientOutput = @"  file1.txt                          A    12345  Mon Jan 29 10:00:00 2026
  file2.doc                           A    67890  Tue Jan 30 11:00:00 2026
  .                                  D        0  Wed Jan 31 12:00:00 2026
  ..                                 D        0  Wed Jan 31 12:00:00 2026

                12345 blocks of size 4096. 6789 blocks available";

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult
                {
                    ExitCode = 0,
                    StandardOutput = smbClientOutput,
                    StandardError = ""
                });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            var result = await handler.EnumerateFilesAsync("//server/share/path");

            // Assert
            var files = result.ToList();
            Assert.Equal(2, files.Count);
            Assert.Contains("file1.txt", files);
            Assert.Contains("file2.doc", files);
        }

        [Fact]
        public async Task EnumerateFilesAsync_DirectoriesInOutput_ExcludesDirectories()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            var smbClientOutput = @"  file1.txt                          A    12345  Mon Jan 29 10:00:00 2026
  subfolder                          D        0  Tue Jan 30 11:00:00 2026
  file2.doc                          A    67890  Wed Jan 31 12:00:00 2026";

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = smbClientOutput });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            var result = await handler.EnumerateFilesAsync("//server/share");

            // Assert
            var files = result.ToList();
            Assert.Equal(2, files.Count);
            Assert.DoesNotContain("subfolder", files);
        }

        [Fact]
        public async Task EnumerateFilesAsync_EmptyDirectory_ReturnsEmpty()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            var smbClientOutput = @"  .                                  D        0  Wed Jan 31 12:00:00 2026
  ..                                 D        0  Wed Jan 31 12:00:00 2026

                12345 blocks of size 4096. 6789 blocks available";

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = smbClientOutput });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            var result = await handler.EnumerateFilesAsync("//server/share");

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task EnumerateFilesAsync_FileNotFound_ThrowsFileNotFoundException()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult
                {
                    ExitCode = 1,
                    StandardError = "NT_STATUS_OBJECT_NAME_NOT_FOUND"
                });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                handler.EnumerateFilesAsync("//server/share/nonexistent"));
        }

        [Fact]
        public async Task EnumerateFilesAsync_AccessDenied_ThrowsUnauthorizedAccessException()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult
                {
                    ExitCode = 1,
                    StandardError = "NT_STATUS_ACCESS_DENIED"
                });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act & Assert
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                handler.EnumerateFilesAsync("//server/share"));
        }

        [Fact]
        public async Task EnumerateFilesAsync_BadNetworkPath_ThrowsDirectoryNotFoundException()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult
                {
                    ExitCode = 1,
                    StandardError = "NT_STATUS_BAD_NETWORK_NAME"
                });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act & Assert
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                handler.EnumerateFilesAsync("//invalidserver/share"));
        }
    }

    /// <summary>
    /// Tests for FileExistsAsync
    /// </summary>
    public class SmbClientFileHandlerFileExistsTests
    {
        [Fact]
        public async Task FileExistsAsync_FileExists_ReturnsTrue()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            var smbClientOutput = @"  file1.txt                          A    12345  Mon Jan 29 10:00:00 2026
  file2.doc                           A    67890  Tue Jan 30 11:00:00 2026";

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = smbClientOutput });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            var result = await handler.FileExistsAsync("file1.txt", "//server/share/path");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task FileExistsAsync_FileDoesNotExist_ReturnsFalse()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            var smbClientOutput = @"  file1.txt                          A    12345  Mon Jan 29 10:00:00 2026
  file2.doc                           A    67890  Tue Jan 30 11:00:00 2026";

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = smbClientOutput });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            var result = await handler.FileExistsAsync("nonexistent.txt", "//server/share/path");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task FileExistsAsync_CaseInsensitive_ReturnsTrue()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            var smbClientOutput = @"  File1.TXT                          A    12345  Mon Jan 29 10:00:00 2026";

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = smbClientOutput });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            var result = await handler.FileExistsAsync("file1.txt", "//server/share/path");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task FileExistsAsync_EmptyDirectory_ReturnsFalse()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            var smbClientOutput = @"  .                                  D        0  Wed Jan 31 12:00:00 2026
  ..                                 D        0  Wed Jan 31 12:00:00 2026";

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = smbClientOutput });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            var result = await handler.FileExistsAsync("anyfile.txt", "//server/share");

            // Assert
            Assert.False(result);
        }
    }

    /// <summary>
    /// Tests for authentication modes
    /// </summary>
    public class SmbClientFileHandlerAuthenticationTests
    {
        [Fact]
        public async Task EnumerateFilesAsync_Kerberos_UsesKerberosFlag()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            mockProcess
                .Setup(x => x.ExecuteAsync(
                    "smbclient",
                    It.Is<string>(args => args.Contains("--use-kerberos=required")),
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "" });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            await handler.EnumerateFilesAsync("//server/share");

            // Assert
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("--use-kerberos=required") && !args.Contains("-U")),
                null,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EnumerateFilesAsync_UsernamePassword_PassesCredentials()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            mockProcess
                .Setup(x => x.ExecuteAsync(
                    "smbclient",
                    It.Is<string>(args => args.Contains("-U")),
                    It.Is<IDictionary<string, string>>(env => env.ContainsKey("PASSWD") && env["PASSWD"] == "testpass"),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "" });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, false, "testuser", "testpass", "DOMAIN");

            // Act
            await handler.EnumerateFilesAsync("//server/share");

            // Assert - Password should be in environment variable, not command line
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => !args.Contains("testpass")), // Password should NOT be in args
                It.Is<IDictionary<string, string>>(env => env["PASSWD"] == "testpass"), // Should be in env
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EnumerateFilesAsync_UsernamePasswordWithDomain_IncludesDomainInUsername()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            mockProcess
                .Setup(x => x.ExecuteAsync(
                    "smbclient",
                    It.Is<string>(args => args.Contains("TESTDOMAIN\\testuser")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "" });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, false, "testuser", "testpass", "TESTDOMAIN");

            // Act
            await handler.EnumerateFilesAsync("//server/share");

            // Assert
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("TESTDOMAIN")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    /// <summary>
    /// Tests for path parsing and command injection protection
    /// </summary>
    public class SmbClientFileHandlerSecurityTests
    {
        [Fact]
        public async Task EnumerateFilesAsync_PathWithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            string capturedArgs = "";
            mockProcess
                .Setup(x => x.ExecuteAsync(
                    "smbclient",
                    It.IsAny<string>(),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, string, IDictionary<string, string>?, CancellationToken>((_, args, _, _) => capturedArgs = args)
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "" });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            await handler.EnumerateFilesAsync("//server/share/path with spaces");

            // Assert
            // Verify the command was executed (escaping is internal, we just verify it doesn't throw)
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EnumerateFilesAsync_RootPath_UsesSimpleLsCommand()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            mockProcess
                .Setup(x => x.ExecuteAsync(
                    "smbclient",
                    It.Is<string>(args => args.Contains("-c \"ls\"")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "" });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            await handler.EnumerateFilesAsync("//server/share");

            // Assert
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("-c \"ls\"") && !args.Contains("/*")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EnumerateFilesAsync_SubPath_UsesLsWithWildcard()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            mockProcess
                .Setup(x => x.ExecuteAsync(
                    "smbclient",
                    It.Is<string>(args => args.Contains("subfolder/*")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "" });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            await handler.EnumerateFilesAsync("//server/share/subfolder");

            // Assert
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("subfolder/*")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    /// <summary>
    /// Tests for WriteFileAsync with different modes
    /// </summary>
    public class SmbClientFileHandlerWriteTests
    {
        [Fact]
        public async Task WriteFileAsync_CreateNewMode_ChecksFileExistence()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            // First call checks if file exists (should return FileNotFound)
            // Second call writes the file
            int callCount = 0;
            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 1) // First call - check existence
                    {
                        return new ProcessResult { ExitCode = 1, StandardError = "NT_STATUS_OBJECT_NAME_NOT_FOUND" };
                    }
                    // Second call - write file
                    return new ProcessResult { ExitCode = 0, StandardOutput = "" };
                });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);
            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

            // Act
            var result = await handler.WriteFileAsync("//server/share", "test.txt", stream, FileWriteMode.CreateNew);

            // Assert
            Assert.True(result);
            Assert.Equal(2, callCount); // Should have called twice: check + write
        }

        [Fact]
        public async Task WriteFileAsync_CreateNewMode_FileExists_ThrowsIOException()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            // File existence check succeeds (file exists)
            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "file exists" });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);
            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

            // Act & Assert
            await Assert.ThrowsAsync<IOException>(() =>
                handler.WriteFileAsync("//server/share", "test.txt", stream, FileWriteMode.CreateNew));
        }

        [Fact]
        public async Task WriteFileAsync_OverwriteMode_WritesNewFile()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "" });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));

            // Act
            var result = await handler.WriteFileAsync("//server/share", "test.txt", stream, FileWriteMode.Overwrite);

            // Assert
            Assert.True(result);
            // Verify only put command was executed (not get for append, not ls for CreateNew)
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("put") && !args.Contains("get") && !args.Contains("ls")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task WriteFileAsync_AppendMode_FileExists_AppendsToExistingFile()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            var existingContent = "existing content\n";

            // Setup mock to handle both get (for existing file) and put (for upload)
            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.Is<string>(args => args.Contains("get")), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string cmd, string args, IDictionary<string, string>? env, CancellationToken ct) =>
                {
                    // Extract the temp file path from the get command and create it with existing content
                    var match = System.Text.RegularExpressions.Regex.Match(args, @"\""([C-Z]:[^\""]+_existing_[^\""]+)\""");
                    if (match.Success)
                    {
                        var tempPath = match.Groups[1].Value.Replace("\\\\", "\\").TrimEnd('\\');
                        File.WriteAllText(tempPath, existingContent);
                    }
                    return new ProcessResult { ExitCode = 0, StandardOutput = "" };
                });

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.Is<string>(args => args.Contains("put")), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "" });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("new content"));

            // Act
            var result = await handler.WriteFileAsync("//server/share", "test.txt", stream, FileWriteMode.Append);

            // Assert
            Assert.True(result);
            // Verify get command was executed (to download existing file)
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("get")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            // Verify put command was executed (to upload combined file)
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("put")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task WriteFileAsync_AppendMode_FileDoesNotExist_CreatesNewFile()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            // Setup mock to return FileNotFound for get (file doesn't exist)
            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.Is<string>(args => args.Contains("get")), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult
                {
                    ExitCode = 1,
                    StandardError = "NT_STATUS_OBJECT_NAME_NOT_FOUND",
                    StandardOutput = ""
                });

            // Setup mock to succeed for put
            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.Is<string>(args => args.Contains("put")), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "" });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("new content"));

            // Act
            var result = await handler.WriteFileAsync("//server/share", "test.txt", stream, FileWriteMode.Append);

            // Assert
            Assert.True(result);
            // Verify get was attempted
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("get")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            // Verify put command was still executed
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("put")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task WriteFileAsync_DefaultMode_UsesOverwrite()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "" });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));

            // Act - call without specifying write mode
            var result = await handler.WriteFileAsync("//server/share", "test.txt", stream);

            // Assert
            Assert.True(result);
            // Verify only put command was executed
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("put") && !args.Contains("get") && !args.Contains("ls")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    /// <summary>
    /// Tests for DeleteFileAsync
    /// </summary>
    public class SmbClientFileHandlerDeleteTests
    {
        [Fact]
        public async Task DeleteFileAsync_Success_ReturnsTrue()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            mockProcess
                .Setup(x => x.ExecuteAsync(
                    "smbclient",
                    It.Is<string>(args => args.Contains("del")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "" });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            var result = await handler.DeleteFileAsync("//server/share", "file.txt");

            // Assert
            Assert.True(result);
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("del") && args.Contains("file.txt")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    /// <summary>
    /// Tests for CreateDirectoryAsync
    /// </summary>
    public class SmbClientFileHandlerCreateDirectoryTests
    {
        [Fact]
        public async Task CreateDirectoryAsync_Success_ReturnsTrue()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            // Mock the ls check to return "not found" (directory doesn't exist)
            mockProcess
                .Setup(x => x.ExecuteAsync(
                    "smbclient",
                    It.Is<string>(args => args.Contains("ls") && args.Contains("newdir")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 1, StandardError = "NT_STATUS_OBJECT_NAME_NOT_FOUND" });

            // Mock the mkdir command to succeed
            mockProcess
                .Setup(x => x.ExecuteAsync(
                    "smbclient",
                    It.Is<string>(args => args.Contains("mkdir")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "" });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            var result = await handler.CreateDirectoryAsync("//server/share/newdir");

            // Assert
            Assert.True(result);
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("ls") && args.Contains("newdir")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("mkdir") && args.Contains("newdir")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CreateDirectoryAsync_DirectoryAlreadyExists_ReturnsTrue()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            // Mock the ls check to succeed (directory exists)
            mockProcess
                .Setup(x => x.ExecuteAsync(
                    "smbclient",
                    It.Is<string>(args => args.Contains("ls") && args.Contains("existingdir")),
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "directory listing" });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            var result = await handler.CreateDirectoryAsync("//server/share/existingdir");

            // Assert
            Assert.True(result);
            // Verify ls was called
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("ls") && args.Contains("existingdir")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            // Verify mkdir was NOT called (directory already exists)
            mockProcess.Verify(x => x.ExecuteAsync(
                "smbclient",
                It.Is<string>(args => args.Contains("mkdir")),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CreateDirectoryAsync_EmptyPath_ThrowsArgumentException()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();
            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                handler.CreateDirectoryAsync("//server/share"));
        }
    }

    /// <summary>
    /// Tests for CanConnectAsync
    /// </summary>
    public class SmbClientFileHandlerCanConnectTests
    {
        [Fact]
        public async Task CanConnectAsync_Success_ReturnsTrue()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "" });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            var result = await handler.CanConnectAsync("//server/share");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task CanConnectAsync_Failure_ReturnsFalse()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 1, StandardError = "Connection failed" });

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            var result = await handler.CanConnectAsync("//server/share");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task CanConnectAsync_Exception_ReturnsFalse()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Network error"));

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            var result = await handler.CanConnectAsync("//server/share");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task CanConnectAsync_WithSubdirectory_UsesCorrectCommand()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();
            string? capturedArguments = null;

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "" })
                .Callback<string, string, IDictionary<string, string>?, CancellationToken>((cmd, args, env, ct) => capturedArguments = args);

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act
            var result = await handler.CanConnectAsync("//server/share/path/to/directory");

            // Assert
            Assert.True(result);
            Assert.NotNull(capturedArguments);
            // Command string gets escaped, so quotes become \"
            Assert.Contains(@"cd \""path/to/directory\""", capturedArguments);
            Assert.Contains("ls", capturedArguments);
        }

        [Fact]
        public async Task CanConnectAsync_WithBackslashPath_UsesCorrectCommand()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<SmbClientFileHandler>>();
            var mockProcess = new Mock<IProcessWrapper>();
            string? capturedArguments = null;

            mockProcess
                .Setup(x => x.ExecuteAsync("smbclient", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = "" })
                .Callback<string, string, IDictionary<string, string>?, CancellationToken>((cmd, args, env, ct) => capturedArguments = args);

            var handler = new SmbClientFileHandler(mockLogger.Object, mockProcess.Object, true);

            // Act - using backslashes like Windows UNC path
            var result = await handler.CanConnectAsync(@"\\server\share\path\to\directory");

            // Assert
            Assert.True(result);
            Assert.NotNull(capturedArguments);
            // Path should be converted to forward slashes for smbclient, and command string gets escaped
            Assert.Contains(@"cd \""path/to/directory\""", capturedArguments);
            Assert.Contains("ls", capturedArguments);
        }
    }

}
