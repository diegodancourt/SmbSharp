using Microsoft.Extensions.Logging;
using Moq;
using SmbSharp.Business;
using SmbSharp.Business.Interfaces;
using SmbSharp.Enums;

namespace SmbSharp.Tests.Business
{
    /// <summary>
    /// Integration tests for FileHandler using local file system (Windows only).
    /// These tests verify the Windows code path using actual file system operations.
    /// SMB-specific tests require a real SMB share and are better suited for manual/CI integration testing.
    /// </summary>
    public class FileHandlerWindowsIntegrationTests : IDisposable
    {
        private readonly string _testDirectory;
        private FileHandler? _handler;

        public FileHandlerWindowsIntegrationTests()
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
            if (_handler == null)
            {
                if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
                {
                    var mockLogger = new Mock<ILogger<FileHandler>>();
                    var mockSmbClient = new Mock<ISmbClientFileHandler>();
                    mockSmbClient.Setup(x => x.IsSmbClientAvailable()).Returns(true);
                    _handler = new FileHandler(mockLogger.Object, mockSmbClient.Object);
                }
                else
                {
                    throw new PlatformNotSupportedException("Tests can only run on Windows or Linux");
                }
            }

            return _handler;
        }

        [Fact]
        public async Task WriteFileAsync_String_CreatesFile()
        {
            if (!OperatingSystem.IsWindows())
            {
                // Skip on non-Windows as it requires smbclient
                return;
            }

            // Arrange
            var handler = CreateHandler();
            var testFile = Path.Combine(_testDirectory, "test.txt");
            var content = "Hello, World!";

            // Act
            var result = await handler.WriteFileAsync(testFile, content);

            // Assert
            Assert.True(result);
            Assert.True(File.Exists(testFile));
            var actualContent = await File.ReadAllTextAsync(testFile);
            Assert.Equal(content, actualContent);
        }

        [Fact]
        public async Task WriteFileAsync_Stream_CreatesFile()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // Arrange
            var handler = CreateHandler();
            var testFile = Path.Combine(_testDirectory, "test.txt");
            var content = "Hello from stream!";
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

            // Act
            var result = await handler.WriteFileAsync(testFile, stream);

            // Assert
            Assert.True(result);
            Assert.True(File.Exists(testFile));
            var actualContent = await File.ReadAllTextAsync(testFile);
            Assert.Equal(content, actualContent);
        }

        [Fact]
        public async Task WriteFileAsync_OverwriteMode_OverwritesExistingFile()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // Arrange
            var handler = CreateHandler();
            var testFile = Path.Combine(_testDirectory, "test.txt");
            await File.WriteAllTextAsync(testFile, "Original content");

            var newContent = "New content";
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(newContent));

            // Act
            var result = await handler.WriteFileAsync(testFile, stream, FileWriteMode.Overwrite);

            // Assert
            Assert.True(result);
            var actualContent = await File.ReadAllTextAsync(testFile);
            Assert.Equal(newContent, actualContent);
        }

        [Fact]
        public async Task WriteFileAsync_CreateNewMode_ThrowsIfFileExists()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // Arrange
            var handler = CreateHandler();
            var testFile = Path.Combine(_testDirectory, "test.txt");
            await File.WriteAllTextAsync(testFile, "Existing content");

            var newContent = "New content";
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(newContent));

            // Act & Assert
            await Assert.ThrowsAsync<IOException>(() =>
                handler.WriteFileAsync(testFile, stream, FileWriteMode.CreateNew));
        }

        [Fact]
        public async Task WriteFileAsync_CreateNewMode_SucceedsIfFileDoesNotExist()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // Arrange
            var handler = CreateHandler();
            var testFile = Path.Combine(_testDirectory, "newfile.txt");
            var content = "New file content";
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

            // Act
            var result = await handler.WriteFileAsync(testFile, stream, FileWriteMode.CreateNew);

            // Assert
            Assert.True(result);
            Assert.True(File.Exists(testFile));
            var actualContent = await File.ReadAllTextAsync(testFile);
            Assert.Equal(content, actualContent);
        }

        [Fact]
        public async Task WriteFileAsync_AppendMode_AppendsToExistingFile()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // Arrange
            var handler = CreateHandler();
            var testFile = Path.Combine(_testDirectory, "test.txt");
            var originalContent = "Line 1\n";
            await File.WriteAllTextAsync(testFile, originalContent);

            var appendContent = "Line 2\n";
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(appendContent));

            // Act
            var result = await handler.WriteFileAsync(testFile, stream, FileWriteMode.Append);

            // Assert
            Assert.True(result);
            var actualContent = await File.ReadAllTextAsync(testFile);
            Assert.Equal(originalContent + appendContent, actualContent);
        }

        [Fact]
        public async Task ReadFileAsync_ReadsExistingFile()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // Arrange
            var handler = CreateHandler();
            var testFile = Path.Combine(_testDirectory, "read.txt");
            var content = "Content to read";
            await File.WriteAllTextAsync(testFile, content);

            var directory = Path.GetDirectoryName(testFile)!;
            var fileName = Path.GetFileName(testFile);

            // Act
            await using var stream = await handler.ReadFileAsync(directory, fileName);
            using var reader = new StreamReader(stream);
            var actualContent = await reader.ReadToEndAsync();

            // Assert
            Assert.Equal(content, actualContent);
        }

        [Fact]
        public async Task ReadFileAsync_NonExistentFile_ThrowsFileNotFoundException()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // Arrange
            var handler = CreateHandler();

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                handler.ReadFileAsync(_testDirectory, "nonexistent.txt"));
        }

        [Fact]
        public async Task DeleteFileAsync_DeletesExistingFile()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // Arrange
            var handler = CreateHandler();
            var testFile = Path.Combine(_testDirectory, "todelete.txt");
            await File.WriteAllTextAsync(testFile, "Delete me");

            // Act
            var result = await handler.DeleteFileAsync(testFile);

            // Assert
            Assert.True(result);
            Assert.False(File.Exists(testFile));
        }

        [Fact]
        public async Task DeleteFileAsync_NonExistentFile_DoesNotThrow()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // Arrange
            var handler = CreateHandler();
            var testFile = Path.Combine(_testDirectory, "nonexistent.txt");

            // Act
            var result = await handler.DeleteFileAsync(testFile);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task MoveFileAsync_MovesFile()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // Arrange
            var handler = CreateHandler();
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            var destFile = Path.Combine(_testDirectory, "dest.txt");
            var content = "Move me";
            await File.WriteAllTextAsync(sourceFile, content);

            // Act
            var result = await handler.MoveFileAsync(sourceFile, destFile);

            // Assert
            Assert.True(result);
            Assert.False(File.Exists(sourceFile));
            Assert.True(File.Exists(destFile));
            var actualContent = await File.ReadAllTextAsync(destFile);
            Assert.Equal(content, actualContent);
        }

        [Fact]
        public async Task CreateDirectoryAsync_CreatesDirectory()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // Arrange
            var handler = CreateHandler();
            var newDir = Path.Combine(_testDirectory, "subdir");

            // Act
            var result = await handler.CreateDirectoryAsync(newDir);

            // Assert
            Assert.True(result);
            Assert.True(Directory.Exists(newDir));
        }

        [Fact]
        public async Task CreateDirectoryAsync_ExistingDirectory_DoesNotThrow()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // Arrange
            var handler = CreateHandler();
            var newDir = Path.Combine(_testDirectory, "existingdir");
            Directory.CreateDirectory(newDir);

            // Act
            var result = await handler.CreateDirectoryAsync(newDir);

            // Assert
            Assert.True(result);
            Assert.True(Directory.Exists(newDir));
        }

        [Fact]
        public async Task EnumerateFilesAsync_ReturnsFileNames()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // Arrange
            var handler = CreateHandler();
            await File.WriteAllTextAsync(Path.Combine(_testDirectory, "file1.txt"), "content1");
            await File.WriteAllTextAsync(Path.Combine(_testDirectory, "file2.txt"), "content2");
            await File.WriteAllTextAsync(Path.Combine(_testDirectory, "file3.doc"), "content3");
            Directory.CreateDirectory(Path.Combine(_testDirectory, "subdir")); // Should not be included

            // Act
            var files = await handler.EnumerateFilesAsync(_testDirectory);
            var fileList = files.ToList();

            // Assert
            Assert.Equal(3, fileList.Count);
            Assert.Contains("file1.txt", fileList);
            Assert.Contains("file2.txt", fileList);
            Assert.Contains("file3.doc", fileList);
        }

        [Fact]
        public async Task EnumerateFilesAsync_EmptyDirectory_ReturnsEmpty()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // Arrange
            var handler = CreateHandler();
            var emptyDir = Path.Combine(_testDirectory, "emptydir");
            Directory.CreateDirectory(emptyDir);

            // Act
            var files = await handler.EnumerateFilesAsync(emptyDir);

            // Assert
            Assert.Empty(files);
        }

        [Fact]
        public async Task EnumerateFilesAsync_NonExistentDirectory_ThrowsDirectoryNotFoundException()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // Arrange
            var handler = CreateHandler();
            var nonExistentDir = Path.Combine(_testDirectory, "nonexistent");

            // Act & Assert
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                handler.EnumerateFilesAsync(nonExistentDir));
        }
    }
}
