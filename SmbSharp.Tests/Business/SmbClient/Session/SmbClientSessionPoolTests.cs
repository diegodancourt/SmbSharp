using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmbSharp.Business.SmbClient.Session;
using SmbSharp.Infrastructure.Interfaces;

namespace SmbSharp.Tests.Business.SmbClient.Session
{
    public class SmbClientSessionPoolTests
    {
        private static Mock<IInteractiveProcess> CreateAliveProcessMock(string readResult = "smb: \\> ")
        {
            var mock = new Mock<IInteractiveProcess>();
            mock.SetupGet(p => p.HasExited).Returns(false);
            mock.Setup(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(readResult);
            return mock;
        }

        [Fact]
        public async Task ExecuteAsync_ReusesSameSession_AcrossMultipleCalls_WhenPoolSizeIsOne()
        {
            var factoryMock = new Mock<IInteractiveProcessFactory>();
            factoryMock.Setup(f => f.Create()).Returns(() => CreateAliveProcessMock().Object);

            using var pool = new SmbClientSessionPool(NullLoggerFactory.Instance, factoryMock.Object,
                useKerberos: true, poolSizePerShare: 1);

            await pool.ExecuteAsync("server1", "share1", "ls", "//server1/share1");
            await pool.ExecuteAsync("server1", "share1", "ls", "//server1/share1");
            await pool.ExecuteAsync("server1", "share1", "ls", "//server1/share1");

            // Only one interactive process should ever have been created - the session was reused,
            // not re-authenticated, on subsequent calls.
            factoryMock.Verify(f => f.Create(), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_DistributesAcrossPoolSlots_WhenPoolSizeIsGreaterThanOne()
        {
            var factoryMock = new Mock<IInteractiveProcessFactory>();
            factoryMock.Setup(f => f.Create()).Returns(() => CreateAliveProcessMock().Object);

            using var pool = new SmbClientSessionPool(NullLoggerFactory.Instance, factoryMock.Object,
                useKerberos: true, poolSizePerShare: 3);

            for (var i = 0; i < 3; i++)
            {
                await pool.ExecuteAsync("server1", "share1", "ls", "//server1/share1");
            }

            factoryMock.Verify(f => f.Create(), Times.Exactly(3));
        }

        [Fact]
        public async Task ExecuteAsync_SeparateShares_GetSeparateSessions()
        {
            var factoryMock = new Mock<IInteractiveProcessFactory>();
            factoryMock.Setup(f => f.Create()).Returns(() => CreateAliveProcessMock().Object);

            using var pool = new SmbClientSessionPool(NullLoggerFactory.Instance, factoryMock.Object,
                useKerberos: true, poolSizePerShare: 1);

            await pool.ExecuteAsync("server1", "share1", "ls", "//server1/share1");
            await pool.ExecuteAsync("server2", "share2", "ls", "//server2/share2");

            factoryMock.Verify(f => f.Create(), Times.Exactly(2));
        }

        [Fact]
        public async Task ExecuteAsync_BrokenSession_RecreatesAndRetriesOnce()
        {
            var callCount = 0;
            var factoryMock = new Mock<IInteractiveProcessFactory>();
            factoryMock.Setup(f => f.Create()).Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First session: succeeds on init, then dies (EOF) on the first command.
                    var dying = new Mock<IInteractiveProcess>();
                    dying.SetupGet(p => p.HasExited).Returns(false);
                    dying.SetupSequence(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync("smb: \\> ")
                        .ThrowsAsync(new IOException("EOF"));
                    return dying.Object;
                }

                return CreateAliveProcessMock().Object;
            });

            using var pool = new SmbClientSessionPool(NullLoggerFactory.Instance, factoryMock.Object,
                useKerberos: true, poolSizePerShare: 1);

            await pool.ExecuteAsync("server1", "share1", "ls", "//server1/share1");

            Assert.Equal(2, callCount); // original session + recreated session after the broken retry
        }

        [Fact]
        public void Constructor_InvalidPoolSize_Throws()
        {
            var factoryMock = new Mock<IInteractiveProcessFactory>();
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SmbClientSessionPool(NullLoggerFactory.Instance, factoryMock.Object, true, poolSizePerShare: 0));
        }
    }
}
