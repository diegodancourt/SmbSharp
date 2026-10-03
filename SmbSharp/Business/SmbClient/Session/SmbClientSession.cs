using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SmbSharp.Infrastructure.Interfaces;

namespace SmbSharp.Business.SmbClient.Session
{
    /// <summary>
    /// A single persistent, authenticated smbclient interactive process bound to one (server, share).
    /// Avoids paying the full TCP-connect + SMB-negotiate + Kerberos SPNEGO handshake cost on every
    /// file operation by keeping one authenticated connection open and reusing it for many commands.
    /// </summary>
    internal class SmbClientSession : ISmbClientSession
    {
        // smbclient's interactive prompt looks like "smb: \> " or "smb: \subdir\> " (no trailing
        // newline - it's waiting for input), so we match against the tail of the accumulated output.
        private static readonly Regex PromptRegex = new(@"smb:\s\S*>\s$", RegexOptions.Compiled);

        private readonly ILogger _logger;
        private readonly IInteractiveProcessFactory _processFactory;
        private readonly string _server;
        private readonly string _share;
        private readonly bool _useKerberos;
        private readonly bool _useWsl;
        private readonly string? _username;
        private readonly string? _password;
        private readonly string? _domain;
        private readonly SemaphoreSlim _executionLock = new(1, 1);
        private readonly TimeSpan _initTimeout;
        private readonly TimeSpan _commandTimeout;
        private readonly string? _wslDistribution;

        private IInteractiveProcess? _process;
        private bool _initialized;
        private volatile bool _disposed;

        public SmbClientSession(ILogger logger, IInteractiveProcessFactory processFactory, string server,
            string share, bool useKerberos, string? username = null, string? password = null,
            string? domain = null, bool useWsl = false, TimeSpan? initTimeout = null,
            TimeSpan? commandTimeout = null, string? wslDistribution = null)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            if (processFactory == null)
                throw new ArgumentNullException(nameof(processFactory));
            if (string.IsNullOrWhiteSpace(server))
                throw new ArgumentException("Server cannot be null or empty.", nameof(server));
            if (string.IsNullOrWhiteSpace(share))
                throw new ArgumentException("Share cannot be null or empty.", nameof(share));
            _initTimeout = initTimeout ?? TimeSpan.FromSeconds(30);
            _commandTimeout = commandTimeout ?? TimeSpan.FromMinutes(2);
            _logger = logger;
            _processFactory = processFactory;
            _server = server;
            _share = share;
            _useKerberos = useKerberos;
            _username = username;
            _password = password;
            _domain = domain;
            _useWsl = useWsl;
            _wslDistribution = wslDistribution;
        }

        public bool IsAlive
        {
            get
            {
                if (_disposed || !_initialized || _process == null)
                    return false;

                try
                {
                    return !_process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    // Process.HasExited throws once the underlying Process has been disposed.
                    return false;
                }
            }
        }

        public bool IsBusy => !_disposed && _executionLock.CurrentCount == 0;

        public DateTime LastUsedUtc { get; private set; } = DateTime.UtcNow;

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            var (executable, argumentList, environmentVariables) = BuildConnectArguments();

            _process = _processFactory.Create();
            _process.Start(executable, argumentList, environmentVariables);

            var contextPath = $"//{_server}/{_share}";
            var stopwatch = Stopwatch.StartNew();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_initTimeout);
            // Reads from a child-process pipe aren't reliably cancellable on every platform, so on
            // timeout also kill the process: that closes the pipe and unblocks the pending read.
            var process = _process;
            using var killOnTimeout = timeoutCts.Token.Register(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                    process.Kill();
            });

            string banner;
            try
            {
                banner = await _process.ReadUntilAsync(PromptRegex, timeoutCts.Token);
            }
            catch (Exception ex) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested &&
                                       ex is OperationCanceledException or IOException)
            {
                throw new SmbSessionBrokenException(
                    $"Timed out after {_initTimeout.TotalSeconds:0}s establishing smbclient session for {contextPath}.", ex);
            }
            catch (IOException ex)
            {
                // Surface a recognizable smbclient error (auth failure, bad share, etc.) if present,
                // otherwise fall back to a generic broken-session exception.
                SmbClientErrorClassifier.ThrowIfKnownError(ex.Message, contextPath);
                throw new SmbSessionBrokenException(
                    $"Failed to establish smbclient session for {contextPath} after {stopwatch.ElapsedMilliseconds}ms: {ex.Message}", ex);
            }

            SmbClientErrorClassifier.ThrowIfKnownError(banner, contextPath);

            _initialized = true;
            LastUsedUtc = DateTime.UtcNow;

            _logger.LogInformation("Established persistent smbclient session for {contextPath} in {elapsedMs}ms",
                contextPath, stopwatch.ElapsedMilliseconds);
        }

        public async Task<string> ExecuteAsync(string command, string contextPath,
            CancellationToken cancellationToken = default)
        {
            if (_process == null || !_initialized)
            {
                throw new InvalidOperationException("Session has not been initialized.");
            }

            if (_disposed)
            {
                throw new SmbSessionBrokenException(
                    $"Cannot run command '{command}' because the smbclient session for {contextPath} has been disposed.");
            }
            var process = _process;
            if (process == null)
                throw new InvalidOperationException("Session has not been initialized.");

            if (command.Any(c => char.IsControl(c)) || command.Contains(';'))
                throw new ArgumentException("A pooled smbclient command must be a single line and cannot contain command separators.", nameof(command));

            await _executionLock.WaitAsync(cancellationToken);
            try
            {
                if (!IsAlive)
                {
                    throw new SmbSessionBrokenException(
                        $"Cannot run command '{command}' because the smbclient session for {contextPath} is no longer alive.");
                }

                // Local file paths embedded in commands (e.g. "put <local> <remote>") are Windows
                // paths, but the underlying smbclient process runs inside WSL, so they must be
                // translated to their /mnt/<drive>/... equivalent - mirroring what the non-pooled
                // path already does in SmbClientFileHandler.ExecuteSmbClientCommandAsync.
                var effectiveCommand = _useWsl ? SmbClientPathUtil.ConvertWindowsPathsInCommand(command) : command;
                using var commandCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (_commandTimeout > TimeSpan.Zero)
                    commandCts.CancelAfter(_commandTimeout);
                using var killOnCancellation = commandCts.Token.Register(process.Kill);

                try
                {
                    await process.WriteLineAsync(effectiveCommand, commandCts.Token);
                    var output = await process.ReadUntilAsync(PromptRegex, commandCts.Token);
                    SmbClientErrorClassifier.ThrowIfKnownError(output, contextPath);
                    LastUsedUtc = DateTime.UtcNow;
                    return output;
                }
                catch (OperationCanceledException ex)
                {
                    BreakSession(killProcess: !commandCts.IsCancellationRequested);
                    if (cancellationToken.IsCancellationRequested)
                        throw;
                    throw new SmbSessionBrokenException(
                        $"smbclient command exceeded the {_commandTimeout.TotalSeconds:0}s timeout for {contextPath}.", ex);
                }
                catch (FileNotFoundException)
                {
                    LastUsedUtc = DateTime.UtcNow;
                    throw;
                }
                catch (DirectoryNotFoundException)
                {
                    LastUsedUtc = DateTime.UtcNow;
                    throw;
                }
                catch (IOException ex)
                {
                    BreakSession(killProcess: !commandCts.IsCancellationRequested);
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);
                    if (commandCts.IsCancellationRequested)
                        throw new SmbSessionBrokenException(
                            $"smbclient command exceeded the {_commandTimeout.TotalSeconds:0}s timeout for {contextPath}.", ex);
                    throw new SmbSessionBrokenException(
                        $"smbclient session for {contextPath} ended while running a command: {ex.Message}", ex);
                }
                catch
                {
                    BreakSession(killProcess: !commandCts.IsCancellationRequested);
                    throw;
                }
            }
            finally
            {
                _executionLock.Release();
            }
        }

        private (string executable, List<string> argumentList, IDictionary<string, string> environmentVariables)
            BuildConnectArguments()
        {
            // smbclient never prints its interactive prompt "smb: \> " at all - not just buffered,
            // genuinely never emitted - unless it detects that its stdin is a TTY. Since .NET's
            // Process redirection always presents stdin as a pipe, we allocate a real pseudo-terminal
            // for smbclient via "script" so it behaves as if run interactively.
            var smbclientArgs = new List<string> { "smbclient", $"//{_server}/{_share}" };
            var environment = new Dictionary<string, string>();
            // Keep readline from emitting terminal-control sequences into captured command output.
            environment["TERM"] = "dumb";

            if (_useKerberos)
            {
                smbclientArgs.Add("--use-kerberos=required");
            }
            else
            {
                // Without this, smbclient (Samba >= 4.15 defaults to "client use kerberos = desired")
                // still attempts Kerberos first - DC discovery + KDC round-trips - before falling back
                // to NTLM. When the KDC is slow/unreachable this stalls session setup long enough for
                // the server to drop the connection (NT_STATUS_CONNECTION_DISCONNECTED).
                smbclientArgs.Add("--use-kerberos=off");

                var username = string.IsNullOrEmpty(_domain)
                    ? _username ?? string.Empty
                    : $"{_domain}\\{_username}";

                smbclientArgs.Add("-U");
                smbclientArgs.Add(username);
                environment["PASSWD"] = _password ?? string.Empty;
            }

            var innerCommand = string.Join(' ', smbclientArgs.Select(ShellQuote));
            var scriptArgs = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? new List<string> { "-q", "/dev/null", "sh", "-c", innerCommand }
                : new List<string> { "-qec", innerCommand, "/dev/null" };

            if (_useWsl)
            {
                var argumentList = new List<string>();
                if (!string.IsNullOrWhiteSpace(_wslDistribution))
                {
                    argumentList.Add("-d");
                    argumentList.Add(_wslDistribution);
                }
                argumentList.Add("script");
                argumentList.AddRange(scriptArgs);
                AddWslEnvironment(environment);
                return ("wsl", argumentList, environment);
            }

            return ("script", scriptArgs, environment);
        }

        private static string ShellQuote(string arg)
        {
            // Wrap in single quotes and escape any embedded single quotes for POSIX shells, since the
            // argument list is being flattened into a single command string for "script -c".
            return "'" + arg.Replace("'", "'\\''") + "'";
        }

        private static void AddWslEnvironment(IDictionary<string, string> environment)
        {
            var entries = (Environment.GetEnvironmentVariable("WSLENV") ?? string.Empty)
                .Split(':', StringSplitOptions.RemoveEmptyEntries)
                .Where(entry => !entry.Split('/')[0].Equals("TERM", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (!entries.Any(entry => entry.Equals("PASSWD/u", StringComparison.OrdinalIgnoreCase)))
                entries.Add("PASSWD/u");
            entries.Add("TERM/u");
            environment["WSLENV"] = string.Join(':', entries);
        }

        private void BreakSession(bool killProcess = true)
        {
            _initialized = false;
            if (killProcess)
                _process?.Kill();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _process?.Dispose();
        }
    }
}
