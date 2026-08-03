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
        public async Task InitializeAsync_UsernamePassword_BuildsCredentialsFileArgument()
        {
            var (session, processMock) = CreateSession(useKerberos: false, username: "svc-user", password: "pw",
                domain: "STORMWIND");
            processMock.Setup(p => p.ReadUntilAsync(It.IsAny<Regex>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("smb: \\> ");

            await session.InitializeAsync();

            processMock.Verify(p => p.Start(
                "script",
                It.Is<IEnumerable<string>>(args =>
                    args.Any(a => a.Contains("smbclient") && a.Contains("-A") && a.Contains("//server1/share1"))),
                It.IsAny<IDictionary<string, string>?>()), Times.Once);
        }
    }
}
