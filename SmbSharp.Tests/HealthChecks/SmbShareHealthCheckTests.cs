using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using SmbSharp.Business.Interfaces;
using SmbSharp.HealthChecks;

namespace SmbSharp.Tests.HealthChecks
{
    /// <summary>
    /// Unit tests for SmbShareHealthCheck
    /// </summary>
    public class SmbShareHealthCheckTests
    {
        [Fact]
        public void Constructor_NullFileHandler_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() =>
                new SmbShareHealthCheck(null!, "//server/share"));

            Assert.Equal("fileHandler", exception.ParamName);
        }

        [Fact]
        public void Constructor_NullDirectoryPath_ShouldThrowArgumentNullException()
        {
            // Arrange
            var mockFileHandler = new Mock<IFileHandler>();

            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() =>
                new SmbShareHealthCheck(mockFileHandler.Object, null!));

            Assert.Equal("directoryPath", exception.ParamName);
        }

        [Fact]
        public void Constructor_ValidParameters_ShouldSucceed()
        {
            // Arrange
            var mockFileHandler = new Mock<IFileHandler>();

            // Act
            var healthCheck = new SmbShareHealthCheck(mockFileHandler.Object, "//server/share");

            // Assert
            Assert.NotNull(healthCheck);
        }

        [Fact]
        public async Task CheckHealthAsync_CanConnect_ShouldReturnHealthy()
        {
            // Arrange
            var mockFileHandler = new Mock<IFileHandler>();
            mockFileHandler
                .Setup(x => x.CanConnectAsync("//server/share", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var healthCheck = new SmbShareHealthCheck(mockFileHandler.Object, "//server/share");
            var context = new HealthCheckContext();

            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.Contains("Successfully connected to SMB share: //server/share", result.Description);
            mockFileHandler.Verify(x => x.CanConnectAsync("//server/share", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CheckHealthAsync_CannotConnect_ShouldReturnUnhealthy()
        {
            // Arrange
            var mockFileHandler = new Mock<IFileHandler>();
            mockFileHandler
                .Setup(x => x.CanConnectAsync("//server/share", It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var healthCheck = new SmbShareHealthCheck(mockFileHandler.Object, "//server/share");
            var context = new HealthCheckContext();

            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Contains("Unable to connect to SMB share: //server/share", result.Description);
            mockFileHandler.Verify(x => x.CanConnectAsync("//server/share", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CheckHealthAsync_ThrowsException_ShouldReturnUnhealthy()
        {
            // Arrange
            var mockFileHandler = new Mock<IFileHandler>();
            var expectedException = new IOException("Network error");
            mockFileHandler
                .Setup(x => x.CanConnectAsync("//server/share", It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);

            var healthCheck = new SmbShareHealthCheck(mockFileHandler.Object, "//server/share");
            var context = new HealthCheckContext();

            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Contains("Health check failed for SMB share //server/share", result.Description);
            Assert.Contains("Network error", result.Description);
            Assert.Equal(expectedException, result.Exception);
            mockFileHandler.Verify(x => x.CanConnectAsync("//server/share", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CheckHealthAsync_WithCancellationToken_ShouldPassToken()
        {
            // Arrange
            var mockFileHandler = new Mock<IFileHandler>();
            mockFileHandler
                .Setup(x => x.CanConnectAsync("//server/share", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var healthCheck = new SmbShareHealthCheck(mockFileHandler.Object, "//server/share");
            var context = new HealthCheckContext();
            var cts = new CancellationTokenSource();

            // Act
            await healthCheck.CheckHealthAsync(context, cts.Token);

            // Assert
            mockFileHandler.Verify(x => x.CanConnectAsync("//server/share", cts.Token), Times.Once);
        }

        [Fact]
        public async Task CheckHealthAsync_CancellationRequested_ShouldThrow()
        {
            // Arrange
            var mockFileHandler = new Mock<IFileHandler>();
            mockFileHandler
                .Setup(x => x.CanConnectAsync("//server/share", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());

            var healthCheck = new SmbShareHealthCheck(mockFileHandler.Object, "//server/share");
            var context = new HealthCheckContext();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            var result = await healthCheck.CheckHealthAsync(context, cts.Token);

            // Assert - OperationCanceledException should be caught and returned as Unhealthy
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.NotNull(result.Exception);
        }

        [Fact]
        public async Task CheckHealthAsync_MultipleInvocations_ShouldReturnConsistentResults()
        {
            // Arrange
            var mockFileHandler = new Mock<IFileHandler>();
            mockFileHandler
                .Setup(x => x.CanConnectAsync("//server/share", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var healthCheck = new SmbShareHealthCheck(mockFileHandler.Object, "//server/share");
            var context = new HealthCheckContext();

            // Act
            var result1 = await healthCheck.CheckHealthAsync(context);
            var result2 = await healthCheck.CheckHealthAsync(context);
            var result3 = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.Equal(HealthStatus.Healthy, result1.Status);
            Assert.Equal(HealthStatus.Healthy, result2.Status);
            Assert.Equal(HealthStatus.Healthy, result3.Status);
            mockFileHandler.Verify(x => x.CanConnectAsync("//server/share", It.IsAny<CancellationToken>()), Times.Exactly(3));
        }
    }
}
