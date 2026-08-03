using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmbSharp.Business;

namespace SmbSharp.Tests.Business
{
    /// <summary>
    /// Manual, opt-in integration tests that exercise the persistent session-pool feature against a
    /// real SMB3 share via smbclient running under WSL (since this dev box is Windows).
    ///
    /// These tests are NOT part of the normal CI/local test run. They only execute when the required
    /// environment variables are set, so `dotnet test` is safe to run without them configured.
    ///
    /// Required environment variables:
    ///   SMBSHARP_TEST_SERVER   - SMB server hostname (e.g. 2TBWPAYMENTS01.progcloud.net)
    ///   SMBSHARP_TEST_SHARE    - Share name (e.g. Shared)
    ///   SMBSHARP_TEST_PATH     - Path under the share to read/write test files in (e.g. FakeSftp\Wex)
    ///   SMBSHARP_TEST_USERNAME - Domain service account username
    ///   SMBSHARP_TEST_PASSWORD - Domain service account password
    ///   SMBSHARP_TEST_DOMAIN   - (optional) domain, e.g. STORMWIND
    ///
    /// Example (PowerShell), targeting the QA Wex share:
    ///   $env:SMBSHARP_TEST_SERVER = "2TBWPAYMENTS01.progcloud.net"
    ///   $env:SMBSHARP_TEST_SHARE = "Shared"
    ///   $env:SMBSHARP_TEST_PATH = "FakeSftp\Wex"
    ///   $env:SMBSHARP_TEST_USERNAME = "svc-account"
    ///   $env:SMBSHARP_TEST_PASSWORD = "***"
    ///   dotnet test --filter FullyQualifiedName~FileHandlerSessionPoolManualIntegrationTests
    ///
    /// Requires WSL with smbclient installed and network/DNS reachability to the target server.
    /// </summary>
    public class FileHandlerSessionPoolManualIntegrationTests
    {
        private static string? Server => Environment.GetEnvironmentVariable("SMBSHARP_TEST_SERVER");
        private static string? Share => Environment.GetEnvironmentVariable("SMBSHARP_TEST_SHARE");
        private static string? RelativePath => Environment.GetEnvironmentVariable("SMBSHARP_TEST_PATH");
        private static string? Username => Environment.GetEnvironmentVariable("SMBSHARP_TEST_USERNAME");
        private static string? Password => Environment.GetEnvironmentVariable("SMBSHARP_TEST_PASSWORD");
        private static string? Domain => Environment.GetEnvironmentVariable("SMBSHARP_TEST_DOMAIN");

        private static bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Server) &&
            !string.IsNullOrWhiteSpace(Share) &&
            !string.IsNullOrWhiteSpace(Username) &&
            !string.IsNullOrWhiteSpace(Password);

        private static string RemoteDirectory =>
            string.IsNullOrWhiteSpace(RelativePath)
                ? $@"\\{Server}\{Share}"
                : $@"\\{Server}\{Share}\{RelativePath}";

        private FileHandler CreateHandler(bool useSessionPool)
        {
            var loggerFactory = NullLoggerFactory.Instance;
            return FileHandler.CreateWithCredentials(
                Username!,
                Password!,
                Domain,
                loggerFactory,
                useWsl: true,
                useSessionPool: useSessionPool,
                sessionPoolSize: 3,
                sessionIdleTimeout: TimeSpan.FromMinutes(5));
        }

        [Fact]
        public async Task CanConnectAsync_ToRealShare_Succeeds()
        {
            if (!IsConfigured)
            {
                return; // Skip: environment variables not configured, see class doc comment.
            }

            var handler = CreateHandler(useSessionPool: true);

            var connected = await handler.CanConnectAsync(RemoteDirectory);

            Assert.True(connected, $"Expected to connect to {RemoteDirectory} via WSL smbclient.");
        }

        [Fact]
        public async Task WriteReadDelete_RoundTrip_WithSessionPool_Succeeds()
        {
            if (!IsConfigured)
            {
                return; // Skip: environment variables not configured.
            }

            var handler = CreateHandler(useSessionPool: true);
            var fileName = $"smbsharp-pool-test-{Guid.NewGuid():N}.txt";
            var remoteFilePath = Path.Combine(RemoteDirectory, fileName);
            var content = $"SmbSharp session pool manual test - {DateTimeOffset.UtcNow:O}";

            try
            {
                using (var writeStream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
                {
                    var written = await handler.WriteFileAsync(remoteFilePath, writeStream);
                    Assert.True(written);
                }

                await using var readStream = await handler.ReadFileAsync(RemoteDirectory, fileName);
                using var reader = new StreamReader(readStream);
                var actualContent = await reader.ReadToEndAsync();

                Assert.Equal(content, actualContent);
            }
            finally
            {
                try
                {
                    await handler.DeleteFileAsync(remoteFilePath);
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
        }

        /// <summary>
        /// Runs a batch of sequential CanConnectAsync calls with the session pool enabled vs. disabled,
        /// and asserts pooling is meaningfully faster - this is the whole point of the feature: avoiding
        /// a full connect/negotiate/Kerberos-or-NTLM handshake on every single call.
        /// </summary>
        [Fact]
        public async Task RepeatedCalls_WithSessionPool_AreFasterThanWithoutPool()
        {
            if (!IsConfigured)
            {
                return; // Skip: environment variables not configured.
            }

            const int iterations = 5;

            // Warm up both handlers once so first-connection cost doesn't skew either measurement.
            var pooledHandler = CreateHandler(useSessionPool: true);
            await pooledHandler.CanConnectAsync(RemoteDirectory);

            var perCallHandler = CreateHandler(useSessionPool: false);
            await perCallHandler.CanConnectAsync(RemoteDirectory);

            var pooledElapsed = await TimeIterations(pooledHandler, iterations);
            var perCallElapsed = await TimeIterations(perCallHandler, iterations);

            Assert.True(
                pooledElapsed < perCallElapsed,
                $"Expected session-pooled calls ({pooledElapsed.TotalMilliseconds}ms for {iterations} calls) " +
                $"to be faster than per-call smbclient invocations ({perCallElapsed.TotalMilliseconds}ms).");
        }

        private async Task<TimeSpan> TimeIterations(FileHandler handler, int iterations)
        {
            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                await handler.CanConnectAsync(RemoteDirectory);
            }
            stopwatch.Stop();
            return stopwatch.Elapsed;
        }
    }
}
