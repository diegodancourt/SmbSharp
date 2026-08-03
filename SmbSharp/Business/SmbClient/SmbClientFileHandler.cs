using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SmbSharp.Business.Interfaces;
using SmbSharp.Business.SmbClient.Session;
using SmbSharp.Enums;
using SmbSharp.Infrastructure.Interfaces;
using SmbSharp.Models;

namespace SmbSharp.Business.SmbClient
{
    internal class SmbClientFileHandler : ISmbClientFileHandler
    {
        private readonly ILogger<SmbClientFileHandler> _logger;
        private readonly IProcessWrapper _processWrapper;
        private readonly ISmbClientSessionPool? _sessionPool;
        private readonly bool _useKerberos;
        private readonly bool _useWsl;
        private readonly string? _username;
        private readonly string? _password;
        private readonly string? _domain;

        private static readonly Regex SmbPathRegexInstance =
            new(@"^[/\\]{2}([^/\\]+)[/\\]([^/\\]+)(?:[/\\](.*))?$", RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegexInstance = new(@"\s+", RegexOptions.Compiled);

        // Matches smbclient ls output lines: 2 leading spaces, filename (may contain spaces),
        // 2+ spaces separator, attribute flags (capital letters), then size digit
        private static readonly Regex SmbLsLineRegexInstance =
            new(@"^\s{2}(.+?)\s{2,}([A-Z]+)\s+\d+", RegexOptions.Compiled);

        // Cache for smbclient availability check
        private static bool? _smbClientAvailable;
        private static readonly object _smbClientCheckLock = new();

        public bool IsSmbClientAvailable()
        {
            // Use cached result if available
            if (_smbClientAvailable.HasValue)
                return _smbClientAvailable.Value;

            lock (_smbClientCheckLock)
            {
                // Double-check after acquiring lock
                if (_smbClientAvailable.HasValue)
                    return _smbClientAvailable.Value;

                try
                {
                    ProcessResult result;
                    if (_useWsl)
                    {
                        // Check smbclient availability through WSL
                        var args = new List<string> { "smbclient", "--version" };
                        result = Task.Run(() => _processWrapper.ExecuteAsync("wsl", args)).Result;
                    }
                    else
                    {
                        result = Task.Run(() => _processWrapper.ExecuteAsync("smbclient", "--version")).Result;
                    }

                    _smbClientAvailable = result.ExitCode == 0;
                    return _smbClientAvailable.Value;
                }
                catch
                {
                    _smbClientAvailable = false;
                    return false;
                }
            }
        }

        public SmbClientFileHandler(ILogger<SmbClientFileHandler> logger, IProcessWrapper processWrapper,
            bool useKerberos, string? username = null, string? password = null,
            string? domain = null, bool useWsl = false, ISmbClientSessionPool? sessionPool = null)
        {
            _logger = logger;
            _processWrapper = processWrapper ?? throw new ArgumentNullException(nameof(processWrapper));
            _sessionPool = sessionPool;
            _useKerberos = useKerberos;
            _useWsl = useWsl;
            if (!useKerberos && (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)))
            {
                _logger.LogError("Username and Password must be provided when not using Kerberos authentication.");
                throw new ArgumentException(
                    "Username and Password must be provided when not using Kerberos authentication.");
            }

            _username = username;
            _password = password;
            _domain = domain;
        }

        public async Task<IEnumerable<string>> EnumerateFilesAsync(string smbPath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await EnumerateLsEntriesAsync(smbPath, includeDirectories: false, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enumerating files in SMB path: {SmbPath}", smbPath);
                throw;
            }
        }

        public async Task<IEnumerable<string>> EnumerateDirectoriesAsync(string smbPath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await EnumerateLsEntriesAsync(smbPath, includeDirectories: true, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enumerating directories in SMB path: {SmbPath}", smbPath);
                throw;
            }
        }

        private async Task<IEnumerable<string>> EnumerateLsEntriesAsync(string smbPath, bool includeDirectories,
            CancellationToken cancellationToken)
        {
            var entries = new List<string>();

            // Parse SMB path: //server/share/path or \\server\share\path
            var (server, share, path) = ParseSmbPath(smbPath);

            var command = string.IsNullOrEmpty(path) ? "ls" : $"ls {path}/*";

            string output;
            try
            {
                output = await ExecuteSmbClientCommandAsync(server, share, command, smbPath, cancellationToken);
            }
            catch (FileNotFoundException) when (!string.IsNullOrEmpty(path))
            {
                // smbclient returns NT_STATUS_NO_SUCH_FILE when ls path/* is run on an empty directory.
                // Verify the directory itself exists before returning empty; re-throw if it doesn't.
                await ExecuteSmbClientCommandAsync(server, share, $"ls \"{path}\"", smbPath, cancellationToken);
                return entries;
            }

            // Parse smbclient ls output. Format per line (2 leading spaces):
            //   filename                            A      1234  Mon Jan  1 00:00:00 2024
            // Filenames may contain spaces, so we match via regex rather than whitespace split.
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.Contains("blocks of size") || line.Contains("blocks available"))
                    continue;

                var match = SmbLsLineRegexInstance.Match(line);
                if (!match.Success)
                    continue;

                var entryName = match.Groups[1].Value;
                var attributes = match.Groups[2].Value;

                // Always skip . and .. entries
                if (entryName == "." || entryName == "..")
                    continue;

                var isDirectory = attributes.Contains('D');
                if (isDirectory != includeDirectories)
                    continue;

                entries.Add(entryName);
            }

            return entries;
        }

        public async Task<SmbFileInfo> GetFileInfoAsync(string smbPath, string fileName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Parse SMB path: //server/share/path or \\server\share\path
                var (server, share, remotePath) = ParseSmbPath(smbPath);

                var remoteFilePath = string.IsNullOrEmpty(remotePath)
                    ? fileName
                    : $"{remotePath}/{fileName}";

                var command = $"allinfo \"{remoteFilePath}\"";
                var output = await ExecuteSmbClientCommandAsync(server, share, command, smbPath, cancellationToken);

                return ParseAllInfoOutput(output);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving file info for {FileName} in {SmbPath}", fileName, smbPath);
                throw;
            }
        }

        private static SmbFileInfo ParseAllInfoOutput(string output)
        {
            string? altName = null;
            DateTime? createTime = null;
            DateTime? accessTime = null;
            DateTime? writeTime = null;
            DateTime? changeTime = null;
            string? attributes = null;
            var streams = new List<string>();

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');
                var separatorIndex = line.IndexOf(':');
                if (separatorIndex < 0)
                    continue;

                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();

                switch (key)
                {
                    case "altname":
                        altName = value;
                        break;
                    case "create_time":
                        createTime = ParseAllInfoTimestamp(value);
                        break;
                    case "access_time":
                        accessTime = ParseAllInfoTimestamp(value);
                        break;
                    case "write_time":
                        writeTime = ParseAllInfoTimestamp(value);
                        break;
                    case "change_time":
                        changeTime = ParseAllInfoTimestamp(value);
                        break;
                    case "attributes":
                        attributes = value;
                        break;
                    case "stream":
                        streams.Add(value);
                        break;
                }
            }

            return new SmbFileInfo
            {
                AlternateName = altName,
                CreateTime = createTime,
                AccessTime = accessTime,
                WriteTime = writeTime,
                ChangeTime = changeTime,
                Attributes = attributes,
                Streams = streams
            };
        }

        // smbclient's allinfo timestamps typically look like: "Mon Jun 15 03:42:18 2020",
        // sometimes with an AM/PM marker and/or trailing timezone abbreviation appended
        // (e.g. "Mon Jun 15 03:42:18 AM 2020 CEST"). Try a set of known formats, then fall back
        // to stripping the last whitespace-separated token(s) (AM/PM, timezone) and retrying.
        private static readonly string[] AllInfoTimestampFormats =
        {
            "ddd MMM d HH:mm:ss yyyy",
            "ddd MMM dd HH:mm:ss yyyy",
            "ddd MMM d hh:mm:ss tt yyyy",
            "ddd MMM dd hh:mm:ss tt yyyy"
        };

        private static DateTime? ParseAllInfoTimestamp(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var candidate = value.Trim();

            // Try progressively stripping trailing tokens (e.g. a timezone abbreviation) up to twice.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (DateTime.TryParseExact(candidate, AllInfoTimestampFormats, CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces, out var parsed))
                {
                    return parsed;
                }

                var lastSpace = candidate.LastIndexOf(' ');
                if (lastSpace <= 0)
                    break;

                candidate = candidate[..lastSpace];
            }

            return null;
        }

        public async Task<bool> FileExistsAsync(string fileName, string smbPath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var files = await EnumerateFilesAsync(smbPath, cancellationToken);
                return files.Any(f => f.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if file exists: {FileName} in {SmbPath}", fileName, smbPath);
                throw;
            }
        }

        public async Task<Stream> GetFileStreamAsync(string smbPath, string fileName,
            CancellationToken cancellationToken = default)
        {
            // Parse SMB path: //server/share/path or \\server\share\path
            var (server, share, remotePath) = ParseSmbPath(smbPath);

            // Create a temporary local file
            var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{fileName}");

            // Download file using smbclient
            var remoteFilePath = string.IsNullOrEmpty(remotePath)
                ? fileName
                : $"{remotePath}/{fileName}";

            var command = $"get \"{remoteFilePath}\" \"{tempFilePath}\"";
            await ExecuteSmbClientCommandAsync(server, share, command, smbPath, cancellationToken);

            if (!File.Exists(tempFilePath))
            {
                throw new FileNotFoundException(
                    $"Failed to download file {fileName} from {smbPath}");
            }

            // Return a FileStream with DeleteOnClose option to auto-cleanup temp file
            return new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.None, 4096,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        }

        public async Task<bool> WriteFileAsync(string smbPath, string fileName, Stream stream,
            CancellationToken cancellationToken = default)
        {
            return await WriteFileAsync(smbPath, fileName, stream, FileWriteMode.Overwrite, cancellationToken);
        }

        public async Task<bool> WriteFileAsync(string smbPath, string fileName, Stream stream,
            FileWriteMode writeMode, CancellationToken cancellationToken = default)
        {
            // Parse SMB path: //server/share/path or \\server\share\path
            var (server, share, remotePath) = ParseSmbPath(smbPath);

            // Create a temporary local file to upload
            var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{fileName}");

            try
            {
                var remoteFilePath = string.IsNullOrEmpty(remotePath)
                    ? fileName
                    : $"{remotePath}/{fileName}";

                // Handle different write modes
                if (writeMode == FileWriteMode.CreateNew)
                {
                    // Check if file exists first
                    try
                    {
                        var checkCommand = $"ls \"{remoteFilePath}\"";
                        await ExecuteSmbClientCommandAsync(server, share, checkCommand, smbPath, cancellationToken);
                        // If we get here, file exists
                        throw new IOException($"File already exists: {smbPath}/{fileName}");
                    }
                    catch (FileNotFoundException)
                    {
                        // Good - file doesn't exist, continue
                    }
                }
                else if (writeMode == FileWriteMode.Append)
                {
                    // For append mode, download existing file first if it exists
                    try
                    {
                        var existingTempFile =
                            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_existing_{fileName}");
                        var getCommand = $"get \"{remoteFilePath}\" \"{existingTempFile}\"";
                        await ExecuteSmbClientCommandAsync(server, share, getCommand, smbPath, cancellationToken);

                        // Copy existing file to temp file, then append new content
                        await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                        {
                            await using (var existingStream =
                                         new FileStream(existingTempFile, FileMode.Open, FileAccess.Read))
                            {
                                await existingStream.CopyToAsync(fileStream, cancellationToken);
                            }

                            await stream.CopyToAsync(fileStream, cancellationToken);
                        }

                        // Clean up existing temp file
                        if (File.Exists(existingTempFile))
                            File.Delete(existingTempFile);
                    }
                    catch (FileNotFoundException)
                    {
                        // File doesn't exist, just write new content
                        await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write);
                        await stream.CopyToAsync(fileStream, cancellationToken);
                    }
                }
                else // Overwrite
                {
                    // Write stream to temp file
                    await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write);
                    await stream.CopyToAsync(fileStream, cancellationToken);
                }

                // Upload file using smbclient
                var command = $"put \"{tempFilePath}\" \"{remoteFilePath}\"";
                await ExecuteSmbClientCommandAsync(server, share, command, smbPath, cancellationToken);

                return true;
            }
            finally
            {
                // Clean up temp file
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }

        public async Task<bool> DeleteFileAsync(string smbPath, string fileName,
            CancellationToken cancellationToken = default)
        {
            // Parse SMB path: //server/share/path or \\server\share\path
            var (server, share, remotePath) = ParseSmbPath(smbPath);

            // Delete file using smbclient
            var remoteFilePath = string.IsNullOrEmpty(remotePath)
                ? fileName
                : $"{remotePath}/{fileName}";

            var command = $"del \"{remoteFilePath}\"";
            await ExecuteSmbClientCommandAsync(server, share, command, smbPath, cancellationToken);

            return true;
        }

        public async Task<bool> CreateDirectoryAsync(string smbPath, CancellationToken cancellationToken = default)
        {
            // Parse SMB path: //server/share/path or \\server\share\path
            var (server, share, remotePath) = ParseSmbPath(smbPath);

            if (string.IsNullOrEmpty(remotePath))
            {
                throw new ArgumentException("Directory path cannot be empty", nameof(smbPath));
            }

            // Check if directory already exists to make this operation idempotent (consistent with Windows behavior)
            try
            {
                var checkCommand = $"ls \"{remotePath}\"";
                await ExecuteSmbClientCommandAsync(server, share, checkCommand, smbPath, cancellationToken);
                // If we reach here, the directory exists - return true (idempotent behavior)
                return true;
            }
            catch (FileNotFoundException)
            {
                // Directory doesn't exist, proceed to create it
            }

            var command = $"mkdir \"{remotePath}\"";
            await ExecuteSmbClientCommandAsync(server, share, command, smbPath, cancellationToken);

            return true;
        }

        private async Task<string> ExecuteSmbClientCommandAsync(string server, string share, string command,
            string contextPath, CancellationToken cancellationToken = default)
        {
            if (_sessionPool != null)
            {
                // Reuse a pooled, persistent, already-authenticated smbclient session instead of
                // spawning a new process (and re-running the full connect/negotiate/Kerberos
                // handshake) for every single command.
                return await _sessionPool.ExecuteAsync(server, share, command, contextPath, cancellationToken);
            }

            string? credentialsFile = null;

            try
            {
                var argumentList = new List<string>();

                // When using WSL, prepend "smbclient" as the first argument (wsl will be the executable)
                if (_useWsl)
                {
                    argumentList.Add("smbclient");
                }

                // Add server/share
                argumentList.Add($"//{server}/{share}");

                if (_useKerberos)
                {
                    // Use Kerberos authentication (kinit ticket)
                    argumentList.Add("--use-kerberos=required");
                }
                else
                {
                    // Use username/password authentication via credentials file
                    var username = string.IsNullOrEmpty(_domain)
                        ? _username ?? string.Empty
                        : $"{_domain}\\{_username}";

                    // Create temporary credentials file
                    credentialsFile = Path.Combine(Path.GetTempPath(), $"smb_{Guid.NewGuid():N}.creds");
                    await File.WriteAllTextAsync(credentialsFile,
                        $"username={username}\npassword={_password}\n",
                        cancellationToken);

                    try
                    {
                        if (_useWsl)
                        {
                            var chmodArgs = new List<string> { "chmod", "600", ConvertToWslPath(credentialsFile) };
                            await _processWrapper.ExecuteAsync("wsl", chmodArgs, null, cancellationToken);
                        }
                        else
                        {
                            var chmodArgs = new List<string> { "600", credentialsFile };
                            await _processWrapper.ExecuteAsync("chmod", chmodArgs, null, cancellationToken);
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(e, "Failed to set permissions on SMB credentials file.");
                    }

                    // Use credentials file (convert path for WSL if needed)
                    argumentList.Add("-A");
                    argumentList.Add(_useWsl ? ConvertToWslPath(credentialsFile) : credentialsFile);
                }

                // Add command (convert any Windows paths in the command for WSL)
                argumentList.Add("-c");
                argumentList.Add(_useWsl ? ConvertWindowsPathsInCommand(command) : command);

                var executable = _useWsl ? "wsl" : "smbclient";
                var result = await _processWrapper.ExecuteAsync(executable, argumentList, null, cancellationToken);

                if (result.ExitCode == 0)
                {
                    return result.StandardOutput;
                }

                // Try to differentiate error types based on smbclient error messages
                // Check both stdout and stderr as smbclient can output errors to either
                var errorOutput = $"{result.StandardOutput} {result.StandardError}";
                var errorLower = errorOutput.ToLowerInvariant();

                if (errorLower.Contains("does not exist") ||
                    errorLower.Contains("not found") ||
                    errorLower.Contains("nt_status_object_name_not_found") ||
                    errorLower.Contains("nt_status_no_such_file"))
                {
                    throw new FileNotFoundException(
                        $"The specified path was not found on {contextPath}", contextPath);
                }

                if (errorLower.Contains("access denied") ||
                    errorLower.Contains("permission denied") ||
                    errorLower.Contains("nt_status_access_denied") ||
                    errorLower.Contains("logon failure"))
                {
                    throw new UnauthorizedAccessException(
                        $"Access denied to {contextPath}: {result.StandardError}");
                }

                if (errorLower.Contains("bad network path") ||
                    errorLower.Contains("network name not found") ||
                    errorLower.Contains("nt_status_bad_network_name"))
                {
                    throw new DirectoryNotFoundException(
                        $"The network path was not found: {contextPath}");
                }

                // Generic error for everything else
                throw new IOException(
                    $"Failed to execute smbclient command on {contextPath}: {result.StandardError}");
            }
            finally
            {
                // Clean up credentials file
                if (credentialsFile != null)
                {
                    try
                    {
                        File.Delete(credentialsFile);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
        }

        private static (string server, string share, string path) ParseSmbPath(string smbPath)
        {
            // Parse SMB path: //server/share/path or \\server\share\path
            var match = SmbPathRegexInstance.Match(smbPath);
            if (!match.Success)
            {
                throw new ArgumentException($"Invalid SMB path format: {smbPath}");
            }

            var server = match.Groups[1].Value;
            var share = match.Groups[2].Value;
            var path = match.Groups[3].Success ? match.Groups[3].Value.Replace('\\', '/') : "";

            return (server, share, path);
        }

        public async Task<bool> CanConnectAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            try
            {
                // Parse SMB path: //server/share/path or \\server\share\path
                var (server, share, path) = ParseSmbPath(directoryPath);

                // Try to list files to test connection - if path is specified, check that specific directory.
                // IMPORTANT: never use "cd" here. When commands run through the persistent session pool,
                // "cd" permanently changes that pooled session's working directory for every future
                // command that happens to reuse the same slot - other unrelated relative-path commands
                // (e.g. EnumerateFilesAsync's "ls {path}/*") would then resolve against the wrong
                // directory and silently find nothing. Use an absolute (leading "/") path instead, which
                // is always resolved from the share root regardless of the session's current directory.
                var command = string.IsNullOrEmpty(path) ? "ls \"/\"" : $"ls \"/{path}\"";
                await ExecuteSmbClientCommandAsync(server, share, command, directoryPath, cancellationToken);

                return true;
            }
            catch (Exception ex)
            {
                // CanConnectAsync intentionally reports connectivity as a bool (callers, e.g. health
                // checks, only care about success/failure), but swallowing the exception entirely left
                // no diagnostic trail when this fails in production. Log it so the real cause (auth
                // failure, timeout, broken session, etc.) is visible without changing the return contract.
                _logger.LogWarning(ex, "SMB connectivity check failed for {DirectoryPath}", directoryPath);
                return false;
            }
        }

        private static string ConvertToWslPath(string windowsPath) =>
            SmbClientPathUtil.ConvertToWslPath(windowsPath);

        private static string ConvertWindowsPathsInCommand(string command) =>
            SmbClientPathUtil.ConvertWindowsPathsInCommand(command);
    }
}