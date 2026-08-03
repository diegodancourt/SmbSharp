using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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

        private IInteractiveProcess? _process;
        private string? _credentialsFilePath;
        private bool _initialized;

        public SmbClientSession(ILogger logger, IInteractiveProcessFactory processFactory, string server,
            string share, bool useKerberos, string? username = null, string? password = null,
            string? domain = null, bool useWsl = false)
        {
            _logger = logger;
            _processFactory = processFactory;
            _server = server;
            _share = share;
            _useKerberos = useKerberos;
            _username = username;
            _password = password;
            _domain = domain;
            _useWsl = useWsl;
        }

        public bool IsAlive => _initialized && _process is { HasExited: false };

        public DateTime LastUsedUtc { get; private set; } = DateTime.UtcNow;

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            var (executable, argumentList) = BuildConnectArguments();

            _process = _processFactory.Create();
            _process.Start(executable, argumentList);

            var contextPath = $"//{_server}/{_share}";
            string banner;
            try
            {
                banner = await _process.ReadUntilAsync(PromptRegex, cancellationToken);
            }
            catch (IOException ex)
            {
                // Surface a recognizable smbclient error (auth failure, bad share, etc.) if present,
                // otherwise fall back to a generic broken-session exception.
                SmbClientErrorClassifier.ThrowIfKnownError(ex.Message, contextPath);
                throw new SmbSessionBrokenException(
                    $"Failed to establish smbclient session for {contextPath}: {ex.Message}", ex);
            }

            SmbClientErrorClassifier.ThrowIfKnownError(banner, contextPath);

            _initialized = true;
            LastUsedUtc = DateTime.UtcNow;

            _logger.LogDebug("Established persistent smbclient session for {ContextPath}", contextPath);
        }

        public async Task<string> ExecuteAsync(string command, string contextPath,
            CancellationToken cancellationToken = default)
        {
            if (_process == null || !_initialized)
            {
                throw new InvalidOperationException("Session has not been initialized.");
            }

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

                // Unlike the one-shot "smbclient -c 'cmd1;cmd2'" invocation (which smbclient itself
                // splits on ';'), the persistent session talks to smbclient's interactive "smb: \>"
                // prompt, which only ever accepts a single command per line - it does not understand
                // ';'-separated chaining. Callers (e.g. CanConnectAsync building "cd \"path\"; ls")
                // still pass semicolon-joined commands, so split and feed each one to the prompt in
                // turn, returning the output of the last command (mirroring the one-shot behavior).
                var subCommands = effectiveCommand.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim())
                    .Where(c => c.Length > 0)
                    .ToList();

                if (subCommands.Count == 0)
                    subCommands.Add(effectiveCommand);

                string output = string.Empty;
                foreach (var subCommand in subCommands)
                {
                    try
                    {
                        await _process.WriteLineAsync(subCommand, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        throw new SmbSessionBrokenException(
                            $"Failed writing command '{subCommand}' to smbclient session for {contextPath}: {ex.Message}", ex);
                    }

                    try
                    {
                        output = await _process.ReadUntilAsync(PromptRegex, cancellationToken);
                    }
                    catch (IOException ex)
                    {
                        throw new SmbSessionBrokenException(
                            $"smbclient session for {contextPath} ended unexpectedly while running '{subCommand}': {ex.Message}",
                            ex);
                    }

                    SmbClientErrorClassifier.ThrowIfKnownError(output, contextPath);
                }

                LastUsedUtc = DateTime.UtcNow;

                return output;
            }
            finally
            {
                _executionLock.Release();
            }
        }

        private (string executable, List<string> argumentList) BuildConnectArguments()
        {
            // smbclient never prints its interactive prompt "smb: \> " at all - not just buffered,
            // genuinely never emitted - unless it detects that its stdin is a TTY. Since .NET's
            // Process redirection always presents stdin as a pipe, we allocate a real pseudo-terminal
            // for smbclient via "script" so it behaves as if run interactively. "-o0 -e0" (fully
            // unbuffered) is kept as well since a pty is normally line-buffered by the kernel tty
            // layer, but the prompt has no trailing newline so we still want smbclient's own stdio
            // buffering disabled to avoid any additional delay.
            var smbclientArgs = new List<string> { "stdbuf", "-o0", "-e0", "smbclient", $"//{_server}/{_share}" };

            if (_useKerberos)
            {
                smbclientArgs.Add("--use-kerberos=required");
            }
            else
            {
                var username = string.IsNullOrEmpty(_domain)
                    ? _username ?? string.Empty
                    : $"{_domain}\\{_username}";

                _credentialsFilePath = Path.Combine(Path.GetTempPath(), $"smb_session_{Guid.NewGuid():N}.creds");
                File.WriteAllText(_credentialsFilePath, $"username={username}\npassword={_password}\n");
                TryHardenCredentialsFilePermissions(_credentialsFilePath);

                smbclientArgs.Add("-A");
                smbclientArgs.Add(_useWsl ? SmbClientPathUtil.ConvertToWslPath(_credentialsFilePath) : _credentialsFilePath);
            }

            var innerCommand = string.Join(' ', smbclientArgs.Select(ShellQuote));
            var scriptArgs = new List<string> { "-qec", innerCommand, "/dev/null" };

            if (_useWsl)
            {
                // "script" runs inside WSL, so it must be an argument to wsl.exe (with an explicit
                // distro name - relying on the default distro proved unreliable) rather than the
                // top-level executable.
                var argumentList = new List<string> { "-d", "Ubuntu", "script" };
                argumentList.AddRange(scriptArgs);
                return ("wsl", argumentList);
            }

            return ("script", scriptArgs);
        }

        private static string ShellQuote(string arg)
        {
            // Wrap in single quotes and escape any embedded single quotes for POSIX shells, since the
            // argument list is being flattened into a single command string for "script -c".
            return "'" + arg.Replace("'", "'\\''") + "'";
        }

        [ExcludeFromCodeCoverage]
        private void TryHardenCredentialsFilePermissions(string path)
        {
            // Sessions are created rarely (once per pool slot, not per call), so a synchronous
            // one-off chmod here has no meaningful performance impact.
            try
            {
                var chmodTarget = _useWsl ? SmbClientPathUtil.ConvertToWslPath(path) : path;
                var (fileName, args) = _useWsl
                    ? ("wsl", new[] { "chmod", "600", chmodTarget })
                    : ("chmod", new[] { "600", chmodTarget });

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo(fileName)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                foreach (var arg in args)
                {
                    process.StartInfo.ArgumentList.Add(arg);
                }

                process.Start();
                process.WaitForExit(2000);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set permissions on SMB session credentials file.");
            }
        }

        public void Dispose()
        {
            _process?.Dispose();

            if (_credentialsFilePath != null)
            {
                try
                {
                    File.Delete(_credentialsFilePath);
                }
                catch
                {
                    // ignore cleanup errors
                }
            }

            _executionLock.Dispose();
        }
    }
}
