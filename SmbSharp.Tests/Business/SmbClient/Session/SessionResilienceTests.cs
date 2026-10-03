using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmbSharp.Business.SmbClient.Session;
using SmbSharp.Infrastructure.Interfaces;

namespace SmbSharp.Tests.Business.SmbClient.Session
{
    public class SessionResilienceTests
    {
        private const string Prompt = "smb: \\> ";

        private static Mock<IInteractiveProcess> AliveProcess()
        {
            var mock = new Mock<IInteractiveProcess>();
            mock.SetupGet(p => p.HasExited).Returns(false);
            mock.Setup(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>())).ReturnsAsync(Prompt);
            return mock;
        }

        private static Mock<IInteractiveProcess> DisconnectingProcess()
        {
            var mock = new Mock<IInteractiveProcess>();
            mock.SetupGet(p => p.HasExited).Returns(true);
            mock.Setup(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Partial output: session setup failed: NT_STATUS_CONNECTION_DISCONNECTED"));
            return mock;
        }

        private static SmbClientSessionPool CreatePool(Mock<IInteractiveProcessFactory> factory, int poolSize = 1,
            int maxAttempts = 3, bool useKerberos = true) =>
            new(NullLoggerFactory.Instance, factory.Object, useKerberos,
                useKerberos ? null : "user", useKerberos ? null : "pass", useKerberos ? null : "domain",
                poolSizePerShare: poolSize, sessionInitMaxAttempts: maxAttempts, sessionInitRetryDelay: TimeSpan.Zero);

        [Fact]
        public async Task CredentialsSession_DisablesKerberos()
        {
            var process = AliveProcess();
            var factory = new Mock<IInteractiveProcessFactory>();
            factory.Setup(f => f.Create()).Returns(process.Object);
            using var session = new SmbClientSession(NullLogger.Instance, factory.Object, "server1", "share1",
                useKerberos: false, "user", "pass", "domain");

            await session.InitializeAsync();

            process.Verify(p => p.Start("script",
                It.Is<IEnumerable<string>>(args => args.Any(a => a.Contains("'--use-kerberos=off'") &&
                                                    a.Contains("'-U'") && !a.Contains("pass"))),
                It.Is<IDictionary<string, string>?>(environment =>
                    environment != null && environment.ContainsKey("PASSWD") &&
                    environment["PASSWD"] == "pass")), Times.Once);
        }

        [Fact]
        public async Task InitializeAsync_Timeout_KillsProcessAndThrowsBroken()
        {
            var process = new Mock<IInteractiveProcess>();
            process.Setup(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                .Returns<Regex, CancellationToken>(async (_, ct) =>
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return Prompt;
                });
            var factory = new Mock<IInteractiveProcessFactory>();
            factory.Setup(f => f.Create()).Returns(process.Object);
            using var session = new SmbClientSession(NullLogger.Instance, factory.Object, "server1", "share1",
                useKerberos: true, initTimeout: TimeSpan.FromMilliseconds(100));

            var ex = await Assert.ThrowsAsync<SmbSessionBrokenException>(() => session.InitializeAsync());

            Assert.Contains("Timed out", ex.Message);
            process.Verify(p => p.Kill(), Times.Once);
        }

        [Fact]
        public async Task InitializeAsync_CallerCancellation_PropagatesAsCanceled()
        {
            var process = new Mock<IInteractiveProcess>();
            process.Setup(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                .Returns<Regex, CancellationToken>(async (_, ct) =>
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return Prompt;
                });
            var factory = new Mock<IInteractiveProcessFactory>();
            factory.Setup(f => f.Create()).Returns(process.Object);
            using var session = new SmbClientSession(NullLogger.Instance, factory.Object, "server1", "share1",
                useKerberos: true, initTimeout: TimeSpan.FromMinutes(5));
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.InitializeAsync(cts.Token));
        }

        [Fact]
        public async Task IsAlive_AfterDispose_ReturnsFalseInsteadOfThrowing()
        {
            var process = AliveProcess();
            process.SetupGet(p => p.HasExited).Throws(new InvalidOperationException("No process is associated with this object."));
            process.Setup(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>())).ReturnsAsync(Prompt);
            var factory = new Mock<IInteractiveProcessFactory>();
            factory.Setup(f => f.Create()).Returns(process.Object);
            var session = new SmbClientSession(NullLogger.Instance, factory.Object, "server1", "share1", true);
            await session.InitializeAsync();

            Assert.False(session.IsAlive);

            session.Dispose();
            Assert.False(session.IsAlive);
            Assert.False(session.IsBusy);
            await Assert.ThrowsAsync<SmbSessionBrokenException>(() => session.ExecuteAsync("ls", "//server1/share1"));
        }

        [Fact]
        public async Task Pool_InitDisconnect_RetriesThenSucceeds()
        {
            var created = new List<Mock<IInteractiveProcess>>();
            var factory = new Mock<IInteractiveProcessFactory>();
            factory.Setup(f => f.Create()).Returns(() =>
            {
                var m = created.Count < 2 ? DisconnectingProcess() : AliveProcess();
                created.Add(m);
                return m.Object;
            });
            using var pool = CreatePool(factory);

            await pool.ExecuteAsync("server1", "share1", "ls", "//server1/share1");
            Assert.Equal(3, created.Count);
            created[0].Verify(p => p.Dispose(), Times.Once);
            created[1].Verify(p => p.Dispose(), Times.Once);
        }

        [Fact]
        public async Task Pool_InitDisconnect_AllAttemptsFail_ThenNextCallRecovers()
        {
            var failuresRemaining = 3;
            var factory = new Mock<IInteractiveProcessFactory>();
            factory.Setup(f => f.Create()).Returns(() =>
                failuresRemaining-- > 0 ? DisconnectingProcess().Object : AliveProcess().Object);
            using var pool = CreatePool(factory);

            await Assert.ThrowsAsync<SmbSessionBrokenException>(() =>
                pool.ExecuteAsync("server1", "share1", "ls", "//server1/share1"));

            // Previously the slot kept a disposed session and every later call failed with
            // "No process is associated with this object" until idle eviction.
            await pool.ExecuteAsync("server1", "share1", "ls", "//server1/share1");
            factory.Verify(f => f.Create(), Times.Exactly(4));
        }

        [Fact]
        public async Task Pool_InitAuthFailure_DoesNotRetry()
        {
            var process = new Mock<IInteractiveProcess>();
            process.Setup(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("session setup failed: NT_STATUS_LOGON_FAILURE"));
            var factory = new Mock<IInteractiveProcessFactory>();
            factory.Setup(f => f.Create()).Returns(process.Object);
            using var pool = CreatePool(factory);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                pool.ExecuteAsync("server1", "share1", "ls", "//server1/share1"));

            factory.Verify(f => f.Create(), Times.Once);
            process.Verify(p => p.Dispose(), Times.Once);
        }

        [Fact]
        public async Task Pool_SequentialCalls_ReuseSingleLiveSession()
        {
            var factory = new Mock<IInteractiveProcessFactory>();
            factory.Setup(f => f.Create()).Returns(() => AliveProcess().Object);
            using var pool = CreatePool(factory, poolSize: 3);

            for (var i = 0; i < 5; i++)
                await pool.ExecuteAsync("server1", "share1", "ls", "//server1/share1");

            factory.Verify(f => f.Create(), Times.Once);
        }

        [Fact]
        public async Task Pool_ConcurrentCalls_OpenAdditionalSessionWhenLiveOneIsBusy()
        {
            var release = new TaskCompletionSource<string>();
            var firstCommandStarted = new TaskCompletionSource<bool>();
            var created = 0;
            var factory = new Mock<IInteractiveProcessFactory>();
            factory.Setup(f => f.Create()).Returns(() =>
            {
                if (Interlocked.Increment(ref created) > 1)
                    return AliveProcess().Object;

                var slow = new Mock<IInteractiveProcess>();
                slow.SetupGet(p => p.HasExited).Returns(false);
                var reads = 0;
                slow.Setup(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        if (Interlocked.Increment(ref reads) == 1)
                            return Task.FromResult(Prompt); // handshake
                        firstCommandStarted.TrySetResult(true);
                        return release.Task; // block the first command
                    });
                return slow.Object;
            });
            using var pool = CreatePool(factory, poolSize: 3);

            var first = pool.ExecuteAsync("server1", "share1", "ls", "//server1/share1");
            await firstCommandStarted.Task;
            await pool.ExecuteAsync("server1", "share1", "ls", "//server1/share1");
            release.SetResult(Prompt);
            await first;

            Assert.Equal(2, created);
        }
    }
}
