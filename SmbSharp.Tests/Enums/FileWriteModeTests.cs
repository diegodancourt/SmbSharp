using SmbSharp.Enums;

namespace SmbSharp.Tests.Enums
{
    /// <summary>
    /// Unit tests for FileWriteMode enum
    /// </summary>
    public class FileWriteModeTests
    {
        [Fact]
        public void FileWriteMode_ShouldHaveOverwriteValue()
        {
            // Act
            var mode = FileWriteMode.Overwrite;

            // Assert
            Assert.Equal(FileWriteMode.Overwrite, mode);
            Assert.Equal(0, (int)mode);
        }

        [Fact]
        public void FileWriteMode_ShouldHaveCreateNewValue()
        {
            // Act
            var mode = FileWriteMode.CreateNew;

            // Assert
            Assert.Equal(FileWriteMode.CreateNew, mode);
            Assert.Equal(1, (int)mode);
        }

        [Fact]
        public void FileWriteMode_ShouldHaveAppendValue()
        {
            // Act
            var mode = FileWriteMode.Append;

            // Assert
            Assert.Equal(FileWriteMode.Append, mode);
            Assert.Equal(2, (int)mode);
        }

        [Fact]
        public void FileWriteMode_ShouldBeComparable()
        {
            // Arrange
            var overwrite = FileWriteMode.Overwrite;
            var createNew = FileWriteMode.CreateNew;
            var append = FileWriteMode.Append;

            // Assert
            Assert.NotEqual(overwrite, createNew);
            Assert.NotEqual(overwrite, append);
            Assert.NotEqual(createNew, append);
        }

        [Theory]
        [InlineData(FileWriteMode.Overwrite, "Overwrite")]
        [InlineData(FileWriteMode.CreateNew, "CreateNew")]
        [InlineData(FileWriteMode.Append, "Append")]
        public void FileWriteMode_ToString_ShouldReturnCorrectString(FileWriteMode mode, string expected)
        {
            // Act
            var result = mode.ToString();

            // Assert
            Assert.Equal(expected, result);
        }
    }
}
