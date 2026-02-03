using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SmbSharp.Business.Interfaces;
using SmbSharp.Extensions;

namespace SmbSharp.Tests.Extensions
{
    /// <summary>
    /// Unit tests for ServiceCollectionExtensions.AddSmbSharp
    /// </summary>
    public class ServiceCollectionExtensionsAddSmbSharpTests
    {
        [Fact]
        public void AddSmbSharp_WithKerberos_ShouldRegisterService()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLogging();

            // Act
            services.AddSmbSharp();
            var serviceProvider = services.BuildServiceProvider();

            // Assert
            var fileHandler = serviceProvider.GetService<IFileHandler>();
            Assert.NotNull(fileHandler);
        }

        [Fact]
        public void AddSmbSharp_WithKerberos_ShouldRegisterAsScoped()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddSmbSharp();

            // Assert
            var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IFileHandler));
            Assert.NotNull(descriptor);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        }

        [Fact]
        public void AddSmbSharp_WithUsernamePassword_ShouldRegisterService()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLogging();

            // Act
            services.AddSmbSharp("username", "password", "DOMAIN");
            var serviceProvider = services.BuildServiceProvider();

            // Assert
            var fileHandler = serviceProvider.GetService<IFileHandler>();
            Assert.NotNull(fileHandler);
        }

        [Fact]
        public void AddSmbSharp_WithUsernamePassword_ShouldRegisterAsScoped()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddSmbSharp("username", "password", "DOMAIN");

            // Assert
            var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IFileHandler));
            Assert.NotNull(descriptor);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        }

        [Fact]
        public void AddSmbSharp_WithOptions_UseKerberos_ShouldRegisterService()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddSmbSharp(options =>
            {
                options.UseKerberos = true;
            });
            var serviceProvider = services.BuildServiceProvider();

            // Assert
            var fileHandler = serviceProvider.GetService<IFileHandler>();
            Assert.NotNull(fileHandler);
        }

        [Fact]
        public void AddSmbSharp_WithOptions_UsernamePassword_ShouldRegisterService()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddSmbSharp(options =>
            {
                options.UseKerberos = false;
                options.Username = "username";
                options.Password = "password";
                options.Domain = "DOMAIN";
            });
            var serviceProvider = services.BuildServiceProvider();

            // Assert
            var fileHandler = serviceProvider.GetService<IFileHandler>();
            Assert.NotNull(fileHandler);
        }

        [Fact]
        public void AddSmbSharp_WithOptions_NoKerberosNoUsername_ShouldThrowArgumentException()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act & Assert
            Action act = () => services.AddSmbSharp(options =>
            {
                options.UseKerberos = false;
                // Missing username and password
            });

            var exception = Assert.Throws<ArgumentException>(act);
            Assert.Contains("Username and password are required", exception.Message);
        }

        [Fact]
        public void AddSmbSharp_WithOptions_NoKerberosNoPassword_ShouldThrowArgumentException()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act & Assert
            Action act = () => services.AddSmbSharp(options =>
            {
                options.UseKerberos = false;
                options.Username = "username";
                // Missing password
            });

            var exception = Assert.Throws<ArgumentException>(act);
            Assert.Contains("Username and password are required", exception.Message);
        }

        [Fact]
        public void AddSmbSharp_ShouldReturnServiceCollection_ForChaining()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            var result = services.AddSmbSharp();

            // Assert
            Assert.Same(services, result);
        }

        [Fact]
        public void AddSmbSharp_WithServiceProviderAndOptions_UseKerberos_ShouldRegisterService()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddSmbSharp((sp, options) =>
            {
                options.UseKerberos = true;
            });
            var serviceProvider = services.BuildServiceProvider();

            // Assert
            var fileHandler = serviceProvider.GetService<IFileHandler>();
            Assert.NotNull(fileHandler);
        }

        [Fact]
        public void AddSmbSharp_WithServiceProviderAndOptions_UsernamePassword_ShouldRegisterService()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddSmbSharp((sp, options) =>
            {
                options.UseKerberos = false;
                options.Username = "username";
                options.Password = "password";
                options.Domain = "DOMAIN";
            });
            var serviceProvider = services.BuildServiceProvider();

            // Assert
            var fileHandler = serviceProvider.GetService<IFileHandler>();
            Assert.NotNull(fileHandler);
        }

        [Fact]
        public void AddSmbSharp_WithServiceProviderAndOptions_CanAccessServiceProvider()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ITestSecretProvider>(new TestSecretProvider("test-password"));

            // Act
            services.AddSmbSharp((sp, options) =>
            {
                var secretProvider = sp.GetRequiredService<ITestSecretProvider>();
                options.UseKerberos = false;
                options.Username = "username";
                options.Password = secretProvider.GetSecret();
                options.Domain = "DOMAIN";
            });
            var serviceProvider = services.BuildServiceProvider();

            // Assert
            var fileHandler = serviceProvider.GetService<IFileHandler>();
            Assert.NotNull(fileHandler);
        }

        [Fact]
        public void AddSmbSharp_WithServiceProviderAndOptions_NoKerberosNoUsername_ShouldThrowArgumentException()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSmbSharp((sp, options) =>
            {
                options.UseKerberos = false;
                // Missing username and password
            });
            var serviceProvider = services.BuildServiceProvider();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
            {
                var fileHandler = serviceProvider.GetService<IFileHandler>();
            });

            Assert.Contains("Username and password are required", exception.Message);
        }

        [Fact]
        public void AddSmbSharp_WithServiceProviderAndOptions_NoKerberosNoPassword_ShouldThrowArgumentException()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSmbSharp((sp, options) =>
            {
                options.UseKerberos = false;
                options.Username = "username";
                // Missing password
            });
            var serviceProvider = services.BuildServiceProvider();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
            {
                var fileHandler = serviceProvider.GetService<IFileHandler>();
            });

            Assert.Contains("Username and password are required", exception.Message);
        }

        [Fact]
        public void AddSmbSharp_WithServiceProviderAndOptions_ShouldReturnServiceCollection_ForChaining()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            var result = services.AddSmbSharp((sp, options) =>
            {
                options.UseKerberos = true;
            });

            // Assert
            Assert.Same(services, result);
        }
    }

    /// <summary>
    /// Test helper interface for secret provider
    /// </summary>
    public interface ITestSecretProvider
    {
        string GetSecret();
    }

    /// <summary>
    /// Test helper implementation for secret provider
    /// </summary>
    public class TestSecretProvider : ITestSecretProvider
    {
        private readonly string _secret;

        public TestSecretProvider(string secret)
        {
            _secret = secret;
        }

        public string GetSecret() => _secret;
    }

    /// <summary>
    /// Unit tests for ServiceCollectionExtensions.AddSmbShareCheck
    /// </summary>
    public class ServiceCollectionExtensionsHealthCheckTests
    {
        [Fact]
        public void AddSmbShareCheck_ShouldRegisterHealthCheck()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSmbSharp();
            var builder = services.AddHealthChecks();

            // Act
            builder.AddSmbShareCheck("//server/share");

            // Assert
            var healthCheckService = services.FirstOrDefault(d => d.ServiceType == typeof(HealthCheckService));
            Assert.NotNull(healthCheckService);
        }

        [Fact]
        public void AddSmbShareCheck_NullDirectoryPath_ShouldThrowArgumentException()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSmbSharp();
            var builder = services.AddHealthChecks();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
                builder.AddSmbShareCheck(null!));

            Assert.Equal("directoryPath", exception.ParamName);
            Assert.Contains("Directory path cannot be null or empty", exception.Message);
        }

        [Fact]
        public void AddSmbShareCheck_EmptyDirectoryPath_ShouldThrowArgumentException()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSmbSharp();
            var builder = services.AddHealthChecks();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
                builder.AddSmbShareCheck(""));

            Assert.Equal("directoryPath", exception.ParamName);
            Assert.Contains("Directory path cannot be null or empty", exception.Message);
        }

        [Fact]
        public void AddSmbShareCheck_WhitespaceDirectoryPath_ShouldThrowArgumentException()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSmbSharp();
            var builder = services.AddHealthChecks();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
                builder.AddSmbShareCheck("   "));

            Assert.Equal("directoryPath", exception.ParamName);
            Assert.Contains("Directory path cannot be null or empty", exception.Message);
        }

        [Fact]
        public void AddSmbShareCheck_WithCustomName_ShouldUseCustomName()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSmbSharp();
            var builder = services.AddHealthChecks();

            // Act
            builder.AddSmbShareCheck("//server/share", name: "custom_check");
            var serviceProvider = services.BuildServiceProvider();
            var options = serviceProvider.GetRequiredService<IOptions<HealthCheckServiceOptions>>();

            // Assert
            Assert.Contains(options.Value.Registrations, r => r.Name == "custom_check");
        }

        [Fact]
        public void AddSmbShareCheck_WithoutCustomName_ShouldUseDefaultName()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSmbSharp();
            var builder = services.AddHealthChecks();

            // Act
            builder.AddSmbShareCheck("//server/share");
            var serviceProvider = services.BuildServiceProvider();
            var options = serviceProvider.GetRequiredService<IOptions<HealthCheckServiceOptions>>();

            // Assert
            Assert.Contains(options.Value.Registrations, r => r.Name == "smb_share");
        }

        [Fact]
        public void AddSmbShareCheck_ShouldReturnBuilder_ForChaining()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSmbSharp();
            var builder = services.AddHealthChecks();

            // Act
            var result = builder.AddSmbShareCheck("//server/share");

            // Assert
            Assert.Same(builder, result);
        }

        [Fact]
        public void AddSmbShareChecks_ShouldRegisterMultipleHealthChecks()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSmbSharp();
            var builder = services.AddHealthChecks();
            var shares = new Dictionary<string, string>
            {
                { "primary", "//server1/share1" },
                { "backup", "//server2/share2" }
            };

            // Act
            builder.AddSmbShareChecks(shares);
            var serviceProvider = services.BuildServiceProvider();
            var options = serviceProvider.GetRequiredService<IOptions<HealthCheckServiceOptions>>();

            // Assert
            Assert.Contains(options.Value.Registrations, r => r.Name == "smb_share_primary");
            Assert.Contains(options.Value.Registrations, r => r.Name == "smb_share_backup");
        }

        [Fact]
        public void AddSmbShareChecks_NullDictionary_ShouldThrowArgumentException()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSmbSharp();
            var builder = services.AddHealthChecks();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
                builder.AddSmbShareChecks(null!));

            Assert.Equal("shares", exception.ParamName);
            Assert.Contains("Shares dictionary cannot be null or empty", exception.Message);
        }

        [Fact]
        public void AddSmbShareChecks_EmptyDictionary_ShouldThrowArgumentException()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSmbSharp();
            var builder = services.AddHealthChecks();
            var shares = new Dictionary<string, string>();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
                builder.AddSmbShareChecks(shares));

            Assert.Equal("shares", exception.ParamName);
            Assert.Contains("Shares dictionary cannot be null or empty", exception.Message);
        }

        [Fact]
        public void AddSmbShareChecks_ShouldReturnBuilder_ForChaining()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSmbSharp();
            var builder = services.AddHealthChecks();
            var shares = new Dictionary<string, string>
            {
                { "primary", "//server1/share1" }
            };

            // Act
            var result = builder.AddSmbShareChecks(shares);

            // Assert
            Assert.Same(builder, result);
        }
    }
}
