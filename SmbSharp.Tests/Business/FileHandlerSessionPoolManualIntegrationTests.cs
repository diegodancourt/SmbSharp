using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmbSharp.Business;
using SmbSharp.Tests.Util;

namespace SmbSharp.Tests.Business
{
    /// <summary>
    /// Manual, opt-in integration tests that exercise the persistent session-pool feature against a
    /// real SMB3 share via smbclient running under WSL (since this dev box is Windows).
    ///
    /// These tests are NOT part of the normal CI/local test run. They only execute when
    /// SMBSHARP_RUN_LIVE_TESTS=1 is explicitly set and the required environment variables are configured.
    ///
    /// Required environment variables:
    ///   SMBSHARP_TEST_SERVER   - SMB server hostname (e.g. 2TBWPAYMENTS01.progcloud.net)
    ///   SMBSHARP_TEST_SHARE    - Share name (e.g. Shared)
    ///   SMBSHARP_TEST_PATH     - Path under the share to read/write test files in (e.g. FakeSftp\Wex)
    ///   SMBSHARP_TEST_USERNAME - Domain service account username
    ///   SMBSHARP_TEST_PASSWORD - Domain service account password
    ///   SMBSHARP_TEST_DOMAIN   - (optional) domain, e.g. EXAMPLE
    ///
    /// Example (PowerShell), targeting a placeholder test share:
    ///   $env:SMBSHARP_TEST_SERVER = "files.example.com"
    ///   $env:SMBSHARP_TEST_SHARE = "ExampleShare"
    ///   $env:SMBSHARP_TEST_PATH = "manual-tests"
    ///   $env:SMBSHARP_TEST_USERNAME = "svc-account"
    ///   $env:SMBSHARP_TEST_PASSWORD = "***"
    ///   $env:SMBSHARP_RUN_LIVE_TESTS = "1"
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

        private static void EnsureConfigured()
        {
            if (string.IsNullOrWhiteSpace(Server) || string.IsNullOrWhiteSpace(Share) ||
                string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
                throw new InvalidOperationException(
                    "Set the required SMBSHARP_TEST_SERVER, SHARE, PATH, USERNAME and PASSWORD variables.");
        }

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

        [ManualSmbFact]
        public async Task CanConnectAsync_ToRealShare_Succeeds()
        {
            EnsureConfigured();

            var handler = CreateHandler(useSessionPool: true);

            var connected = await handler.CanConnectAsync(RemoteDirectory);

            Assert.True(connected, $"Expected to connect to {RemoteDirectory} via WSL smbclient.");
        }

        [ManualSmbFact]
        public async Task WriteReadDelete_RoundTrip_WithSessionPool_Succeeds()
        {
            EnsureConfigured();

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

        [ManualSmbFact]
        public async Task ListReadWriteDelete_WithSpacesAndUnicodeNames_UsesSessionPool()
        {
            EnsureConfigured();

            using var handler = CreateHandler(useSessionPool: true);
            var invoiceName = $"Invoice not found-{Guid.NewGuid():N}.txt";
            var unicodeName = $"Résumé-日本語-{Guid.NewGuid():N}.txt";
            var names = new[] { invoiceName, unicodeName };
            var contents = new[] { "invoice contents", "unicode filename contents" };
            var paths = names.Select(name => Path.Combine(RemoteDirectory, name)).ToArray();

            try
            {
                for (var i = 0; i < names.Length; i++)
                {
                    using var writeStream = new MemoryStream(Encoding.UTF8.GetBytes(contents[i]));
                    Assert.True(await handler.WriteFileAsync(paths[i], writeStream));
                    if (i == 0)
                        Assert.True(await handler.FileExistsAsync(names[i], RemoteDirectory),
                            $"Could not list the written file '{names[i]}'.");

                    await using var readStream = await handler.ReadFileAsync(RemoteDirectory, names[i]);
                    using var reader = new StreamReader(readStream);
                    Assert.Equal(contents[i], await reader.ReadToEndAsync());
                }
            }
            finally
            {
                foreach (var path in paths)
                    await handler.DeleteFileAsync(path);
            }

            Assert.False(await handler.FileExistsAsync(invoiceName, RemoteDirectory));
            await Assert.ThrowsAsync<FileNotFoundException>(() => handler.ReadFileAsync(RemoteDirectory, unicodeName));
        }

        [ManualSmbFact]
        public async Task MoveFileAsync_SameShare_RenamesFile()
        {
            EnsureConfigured();

            using var handler = CreateHandler(useSessionPool: true);
            var sourceName = $"smbsharp-rename-source-{Guid.NewGuid():N}.txt";
            var destinationName = $"smbsharp-rename-destination-{Guid.NewGuid():N}.txt";
            var sourcePath = Path.Combine(RemoteDirectory, sourceName);
            var destinationPath = Path.Combine(RemoteDirectory, destinationName);
            const string content = "same-share rename contents";

            try
            {
                using (var writeStream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
                    Assert.True(await handler.WriteFileAsync(sourcePath, writeStream));

                Assert.True(await handler.MoveFileAsync(sourcePath, destinationPath));
                Assert.False(await handler.FileExistsAsync(sourceName, RemoteDirectory));
                Assert.True(await handler.FileExistsAsync(destinationName, RemoteDirectory),
                    $"Could not list renamed file '{destinationName}'.");

                await using var readStream = await handler.ReadFileAsync(RemoteDirectory, destinationName);
                using var reader = new StreamReader(readStream);
                Assert.Equal(content, await reader.ReadToEndAsync());
            }
            finally
            {
                await handler.DeleteFileAsync(sourcePath);
                await handler.DeleteFileAsync(destinationPath);
            }
        }

        [ManualSmbFact]
        public async Task MoveFileAsync_ExistingDestination_DoesNotReplaceEitherFile()
        {
            EnsureConfigured();

            using var handler = CreateHandler(useSessionPool: true);
            var sourceName = $"smbsharp-collision-source-{Guid.NewGuid():N}.txt";
            var destinationName = $"smbsharp-collision-destination-{Guid.NewGuid():N}.txt";
            var sourcePath = Path.Combine(RemoteDirectory, sourceName);
            var destinationPath = Path.Combine(RemoteDirectory, destinationName);
            const string sourceContent = "source content must be retained";
            const string destinationContent = "destination content must be retained";

            try
            {
                using (var sourceStream = new MemoryStream(Encoding.UTF8.GetBytes(sourceContent)))
                    Assert.True(await handler.WriteFileAsync(sourcePath, sourceStream));
                using (var destinationStream = new MemoryStream(Encoding.UTF8.GetBytes(destinationContent)))
                    Assert.True(await handler.WriteFileAsync(destinationPath, destinationStream));

                await Assert.ThrowsAsync<IOException>(() => handler.MoveFileAsync(sourcePath, destinationPath));

                await using var sourceReadStream = await handler.ReadFileAsync(RemoteDirectory, sourceName);
                using var sourceReader = new StreamReader(sourceReadStream);
                Assert.Equal(sourceContent, await sourceReader.ReadToEndAsync());

                await using var destinationReadStream = await handler.ReadFileAsync(RemoteDirectory, destinationName);
                using var destinationReader = new StreamReader(destinationReadStream);
                Assert.Equal(destinationContent, await destinationReader.ReadToEndAsync());
            }
            finally
            {
                await handler.DeleteFileAsync(sourcePath);
                await handler.DeleteFileAsync(destinationPath);
            }
        }

        /// <summary>
        /// Runs a batch of sequential CanConnectAsync calls with the session pool enabled vs. disabled,
        /// and asserts pooling is meaningfully faster - this is the whole point of the feature: avoiding
        /// a full connect/negotiate/Kerberos-or-NTLM handshake on every single call.
        /// </summary>
        [ManualSmbFact]
        public async Task RepeatedCalls_WithSessionPool_AreFasterThanWithoutPool()
        {
            EnsureConfigured();

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
