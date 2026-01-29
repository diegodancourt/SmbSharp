using SmbSharp.Options;

namespace SmbSharp.Tests.Options
{
    /// <summary>
    /// Unit tests for SmbSharpOptions
    /// </summary>
    public class SmbSharpOptionsTests
    {
        [Fact]
        public void Constructor_ShouldSetDefaultValues()
        {
            // Act
            var options = new SmbSharpOptions();

            // Assert
            Assert.True(options.UseKerberos);
            Assert.Null(options.Username);
            Assert.Null(options.Password);
            Assert.Null(options.Domain);
        }

        [Fact]
        public void UseKerberos_ShouldBeSettable()
        {
            // Arrange
            var options = new SmbSharpOptions();

            // Act
            options.UseKerberos = false;

            // Assert
            Assert.False(options.UseKerberos);
        }

        [Fact]
        public void Username_ShouldBeSettable()
        {
            // Arrange
            var options = new SmbSharpOptions();

            // Act
            options.Username = "testuser";

            // Assert
            Assert.Equal("testuser", options.Username);
        }

        [Fact]
        public void Password_ShouldBeSettable()
        {
            // Arrange
            var options = new SmbSharpOptions();

            // Act
            options.Password = "testpass";

            // Assert
            Assert.Equal("testpass", options.Password);
        }

        [Fact]
        public void Domain_ShouldBeSettable()
        {
            // Arrange
            var options = new SmbSharpOptions();

            // Act
            options.Domain = "TESTDOMAIN";

            // Assert
            Assert.Equal("TESTDOMAIN", options.Domain);
        }

        [Fact]
        public void Properties_ShouldBeIndependent()
        {
            // Arrange
            var options = new SmbSharpOptions();

            // Act
            options.UseKerberos = false;
            options.Username = "user1";
            options.Password = "pass1";
            options.Domain = "DOMAIN1";

            // Assert
            Assert.False(options.UseKerberos);
            Assert.Equal("user1", options.Username);
            Assert.Equal("pass1", options.Password);
            Assert.Equal("DOMAIN1", options.Domain);
        }
    }
}
