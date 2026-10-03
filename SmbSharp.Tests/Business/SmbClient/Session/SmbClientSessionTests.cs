using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmbSharp.Business.SmbClient.Session;
using SmbSharp.Infrastructure.Interfaces;

namespace SmbSharp.Tests.Business.SmbClient.Session
{
    public class SmbClientSessionTests
    {
        private static (SmbClientSession session, Mock<IInteractiveProcess> processMock) CreateSession(
            bool useKerberos = true, string? username = null, string? password = null, string? domain = null)
        {
            var processMock = new Mock<IInteractiveProcess>();
            processMock.SetupGet(p => p.HasExited).Returns(false);

            var factoryMock = new Mock<IInteractiveProcessFactory>();
            factoryMock.Setup(f => f.Create()).Returns(processMock.Object);

            var session = new SmbClientSession(NullLogger.Instance, factoryMock.Object, "server1", "share1",
                useKerberos, username, password, domain);

            return (session, processMock);
        }

        [Fact]
        public async Task InitializeAsync_ReadsInitialPrompt_MarksSessionAlive()
        {
            var (session, processMock) = CreateSession();
            processMock.Setup(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("Try \"help\" to get a list of possible commands.\nsmb: \\> ");

            await session.InitializeAsync();

            Assert.True(session.IsAlive);
            processMock.Verify(p => p.Start(
                "script",
                It.Is<IEnumerable<string>>(args =>
                    args.Any(a => a.Contains("smbclient") && a.Contains("//server1/share1") && a.Contains("--use-kerberos=required"))),
                It.IsAny<IDictionary<string, string>?>()), Times.Once);
        }

        [Fact]
        public async Task InitializeAsync_LogonFailureBanner_ThrowsUnauthorizedAccessException()
        {
            var (session, processMock) = CreateSession();
            processMock.Setup(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("session setup failed: NT_STATUS_LOGON_FAILURE"));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.InitializeAsync());
            Assert.False(session.IsAlive);
        }

        [Fact]
        public async Task InitializeAsync_UnrecognizedEof_ThrowsSmbSessionBrokenException()
        {
            var (session, processMock) = CreateSession();
            processMock.Setup(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("connection reset by peer"));

            await Assert.ThrowsAsync<SmbSessionBrokenException>(() => session.InitializeAsync());
            Assert.False(session.IsAlive);
        }

        [Fact]
        public async Task ExecuteAsync_AfterInitialize_SendsCommandAndReturnsOutput()
        {
            var (session, processMock) = CreateSession();
            processMock.SetupSequence(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("smb: \\> ")
                .ReturnsAsync("  file1.txt                          A      123  Mon Jan  1 00:00:00 2026\nsmb: \\> ");

            await session.InitializeAsync();
            var output = await session.ExecuteAsync("ls", "//server1/share1");

            Assert.Contains("file1.txt", output);
            processMock.Verify(p => p.WriteLineAsync("ls", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_NoSuchFileOutput_ThrowsFileNotFoundException()
        {
            var (session, processMock) = CreateSession();
            processMock.SetupSequence(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("smb: \\> ")
                .ReturnsAsync("NT_STATUS_OBJECT_NAME_NOT_FOUND listing \\missing\nsmb: \\> ");

            await session.InitializeAsync();

            await Assert.ThrowsAsync<FileNotFoundException>(
                () => session.ExecuteAsync("ls \"missing\"", "//server1/share1/missing"));
        }

        [Fact]
        public void ErrorClassifier_DoesNotTreatOrdinaryNamesAsErrors()
        {
            var listing = "  Invoice not found.pdf                  A      123  Mon Jan  1 00:00:00 2026\n" +
                          "  Access denied notes.txt               A      456  Mon Jan  1 00:00:00 2026";

            SmbClientErrorClassifier.ThrowIfKnownError(listing, "//server1/share1");
        }

        [Fact]
        public void ErrorClassifier_ClassifiesObjectPathNotFound()
        {
            Assert.Throws<FileNotFoundException>(() =>
                SmbClientErrorClassifier.ThrowIfKnownError("NT_STATUS_OBJECT_PATH_NOT_FOUND", "//server1/share1"));
        }

        [Fact]
        public async Task ExecuteAsync_ProcessDiesMidCommand_ThrowsSmbSessionBrokenException_AndMarksNotAlive()
        {
            var (session, processMock) = CreateSession();
            var exited = false;
            processMock.SetupGet(p => p.HasExited).Returns(() => exited);
            processMock.SetupSequence(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("smb: \\> ")
                .ThrowsAsync(new IOException("EOF"));

            await session.InitializeAsync();
            exited = true;

            await Assert.ThrowsAsync<SmbSessionBrokenException>(() => session.ExecuteAsync("ls", "//server1/share1"));
            Assert.False(session.IsAlive);
        }

        [Fact]
        public async Task ExecuteAsync_CancelledRead_KillsAndInvalidatesSession()
        {
            var (session, processMock) = CreateSession();
            var readCount = 0;
            processMock.Setup(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                .Returns<Regex, CancellationToken>(async (_, token) =>
                {
                    if (Interlocked.Increment(ref readCount) == 1)
                        return "smb: \\> ";
                    await Task.Delay(Timeout.Infinite, token);
                    return string.Empty;
                });
            await session.InitializeAsync();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                session.ExecuteAsync("ls", "//server1/share1", cancellation.Token));

            Assert.False(session.IsAlive);
            processMock.Verify(p => p.Kill(), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_CommandTimeout_KillsAndInvalidatesSession()
        {
            var processMock = new Mock<IInteractiveProcess>();
            processMock.SetupGet(p => p.HasExited).Returns(false);
            var commandStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var killed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            processMock.Setup(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    if (!commandStarted.Task.IsCompleted)
                    {
                        commandStarted.SetResult();
                        return Task.FromResult("smb: \\> ");
                    }

                    return WaitForKillAsync(killed.Task);
                });
            processMock.Setup(p => p.Kill()).Callback(() => killed.TrySetResult());
            var factory = new Mock<IInteractiveProcessFactory>();
            factory.Setup(f => f.Create()).Returns(processMock.Object);
            using var session = new SmbClientSession(NullLogger.Instance, factory.Object, "server1", "share1",
                useKerberos: true, commandTimeout: TimeSpan.FromMilliseconds(50));
            await session.InitializeAsync();

            var ex = await Assert.ThrowsAsync<SmbSessionBrokenException>(() =>
                session.ExecuteAsync("ls", "//server1/share1"));

            Assert.Contains("timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(session.IsAlive);
            processMock.Verify(p => p.Kill(), Times.Once);
        }

        private static async Task<string> WaitForKillAsync(Task killed)
        {
            await killed;
            throw new IOException("Process killed.");
        }

        [Fact]
        public async Task InitializeAsync_WslDistribution_IsAppliedToProcessArguments()
        {
            var process = new Mock<IInteractiveProcess>();
            IDictionary<string, string>? processEnvironment = null;
            process.SetupGet(p => p.HasExited).Returns(false);
            process.Setup(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("smb: \\> ");
            process.Setup(p => p.Start(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(),
                    It.IsAny<IDictionary<string, string>?>()))
                .Callback<string, IEnumerable<string>, IDictionary<string, string>?>((_, _, environment) =>
                    processEnvironment = environment);
            var factory = new Mock<IInteractiveProcessFactory>();
            factory.Setup(f => f.Create()).Returns(process.Object);
            using var session = new SmbClientSession(NullLogger.Instance, factory.Object, "server1", "share1",
                useKerberos: true, useWsl: true, wslDistribution: "Debian-test");

            await session.InitializeAsync();

            process.Verify(p => p.Start("wsl",
                It.Is<IEnumerable<string>>(args => args.Take(2).SequenceEqual(new[] { "-d", "Debian-test" })),
                It.IsAny<IDictionary<string, string>?>()), Times.Once);
            Assert.Equal("dumb", processEnvironment!["TERM"]);
            Assert.Contains("TERM/u", processEnvironment["WSLENV"].Split(':'));
        }

        [Fact]
        public async Task ExecuteAsync_SemicolonSeparatedCommand_IsRejectedBeforeDispatch()
        {
            var (session, processMock) = CreateSession();
            processMock.Setup(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("smb: \\> ");

            await session.InitializeAsync();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                session.ExecuteAsync("ls a; del *", "//server1/share1"));
            processMock.Verify(p => p.WriteLineAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task InitializeAsync_UsernamePassword_PassesPasswordOnlyInProcessEnvironment()
        {
            var (session, processMock) = CreateSession(useKerberos: false, username: "svc-user", password: "pw",
                domain: "EXAMPLE");
            processMock.Setup(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("smb: \\> ");

            await session.InitializeAsync();

            processMock.Verify(p => p.Start(
                "script",
                It.Is<IEnumerable<string>>(args =>
                    args.Any(a => a.Contains("smbclient") && a.Contains("'-U'") && a.Contains("//server1/share1"))),
                It.Is<IDictionary<string, string>?>(environment =>
                    environment != null && environment.ContainsKey("PASSWD") && environment["PASSWD"] == "pw")), Times.Once);
        }
    }
}
