using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SmbSharp.Business.Interfaces;
using SmbSharp.Business.SmbClient.Session;
using SmbSharp.Enums;
using SmbSharp.Infrastructure.Interfaces;
using SmbSharp.Models;

namespace SmbSharp.Business.SmbClient
{
    internal class SmbClientFileHandler : ISmbClientFileHandler, ISmbClientFileHandlerMove
    {
        private readonly ILogger<SmbClientFileHandler> _logger;
        private readonly IProcessWrapper _processWrapper;
        private readonly ISmbClientSessionPool? _sessionPool;
        private readonly bool _useKerberos;
        private readonly bool _useWsl;
        private readonly string? _username;
        private readonly string? _password;
        private readonly string? _domain;
        private readonly string? _wslDistribution;

        private static readonly Regex SmbPathRegexInstance =
            new(@"^[/\\]{2}([^/\\]+)[/\\]([^/\\]+)(?:[/\\](.*))?$", RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegexInstance = new(@"\s+", RegexOptions.Compiled);

        // Matches smbclient ls output lines: 2 leading spaces, filename (may contain spaces),
        // 2+ spaces separator, attribute flags (capital letters), then size digit
        private static readonly Regex SmbLsLineRegexInstance =
            new(@"^\s{2}(.+?)\s{2,}([A-Z]+)\s+\d+\s{2,}[A-Z][a-z]{2}\s+[A-Z][a-z]{2}\s+\d{1,2}\b",
                RegexOptions.Compiled);

        private bool _smbClientAvailable;

        public bool IsSmbClientAvailable()
        {
            if (_smbClientAvailable)
                return true;

            var args = new List<string>();
            if (_useWsl)
            {
                AddWslDistribution(args);
                args.Add("smbclient");
            }
            args.Add("--version");
            var executable = _useWsl ? "wsl" : "smbclient";
            try
            {
                var result = _processWrapper.ExecuteAsync(executable, args).GetAwaiter().GetResult();
                _smbClientAvailable = result.ExitCode == 0;
                return _smbClientAvailable;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                _logger.LogWarning(ex, "Could not start {executable} to check smbclient availability.", executable);
                return false;
            }
        }

        public SmbClientFileHandler(ILogger<SmbClientFileHandler> logger, IProcessWrapper processWrapper,
            bool useKerberos, string? username = null, string? password = null,
            string? domain = null, bool useWsl = false, ISmbClientSessionPool? sessionPool = null,
            string? wslDistribution = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            _wslDistribution = wslDistribution;
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
                _logger.LogDebug(ex, "Error enumerating files in SMB path: {smbPath}", smbPath);
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
                _logger.LogDebug(ex, "Error enumerating directories in SMB path: {smbPath}", smbPath);
                throw;
            }
        }

        private async Task<IEnumerable<string>> EnumerateLsEntriesAsync(string smbPath, bool includeDirectories,
            CancellationToken cancellationToken)
        {
            var entries = new List<string>();

            // Parse SMB path: //server/share/path or \\server\share\path
            var (server, share, path) = ParseSmbPath(smbPath);

            var command = string.IsNullOrEmpty(path)
                ? "ls"
                : $"ls {SmbClientCommandBuilder.QuotePath(path, nameof(smbPath))}/*";

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

                var match = MatchSmbLsLine(line);
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
                SmbClientCommandBuilder.ValidateFileName(fileName, nameof(fileName));
                var remoteFilePath = BuildRemoteFilePath(remotePath, fileName);

                var command = $"allinfo {SmbClientCommandBuilder.QuotePath(remoteFilePath, nameof(fileName))}";
                var output = await ExecuteSmbClientCommandAsync(server, share, command, smbPath, cancellationToken);

                return ParseAllInfoOutput(output);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error retrieving file info for {fileName} in {smbPath}", fileName, smbPath);
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
                SmbClientCommandBuilder.ValidateFileName(fileName, nameof(fileName));
                var (server, share, remotePath) = ParseSmbPath(smbPath);
                var remoteFilePath = BuildRemoteFilePath(remotePath, fileName);
                var command = $"ls {SmbClientCommandBuilder.QuotePath(remoteFilePath, nameof(fileName))}";
                try
                {
                    var output = await ExecuteSmbClientCommandAsync(server, share, command, smbPath, cancellationToken);
                    return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(MatchSmbLsLine)
                        .Where(match => match.Success)
                        .Any(match => match.Groups[1].Value.Equals(fileName, StringComparison.OrdinalIgnoreCase) &&
                                      !match.Groups[2].Value.Contains('D'));
                }
                catch (FileNotFoundException)
                {
                    if (!string.IsNullOrEmpty(remotePath))
                        await ExecuteSmbClientCommandAsync(server, share,
                            $"ls {SmbClientCommandBuilder.QuotePath(remotePath, nameof(smbPath))}",
                            smbPath, cancellationToken);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error checking if file exists: {fileName} in {smbPath}", fileName, smbPath);
                throw;
            }
        }

        private static Match MatchSmbLsLine(string line) =>
            SmbLsLineRegexInstance.Match(line.TrimStart('\r'));

        public async Task<Stream> GetFileStreamAsync(string smbPath, string fileName,
            CancellationToken cancellationToken = default)
        {
            // Parse SMB path: //server/share/path or \\server\share\path
            var (server, share, remotePath) = ParseSmbPath(smbPath);
            SmbClientCommandBuilder.ValidateFileName(fileName, nameof(fileName));
            var (tempDirectory, tempFilePath) = await CreatePrivateTempFileAsync(cancellationToken);
            try
            {
                var remoteFilePath = BuildRemoteFilePath(remotePath, fileName);
                var command = $"get {SmbClientCommandBuilder.QuotePath(remoteFilePath, nameof(fileName))} {SmbClientCommandBuilder.QuotePath(tempFilePath, nameof(tempFilePath))}";
                await ExecuteSmbClientCommandAsync(server, share, command, smbPath, cancellationToken);

                for (var attempt = 0; attempt < 20 && !File.Exists(tempFilePath); attempt++)
                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);

                if (!File.Exists(tempFilePath))
                    throw new FileNotFoundException($"Failed to download file {fileName} from {smbPath}");

                var fileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.None, 4096,
                    FileOptions.DeleteOnClose | FileOptions.Asynchronous);
                return new TemporaryDirectoryStream(fileStream, tempDirectory);
            }
            catch
            {
                DeleteTemporaryDirectory(tempDirectory);
                throw;
            }
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
            SmbClientCommandBuilder.ValidateFileName(fileName, nameof(fileName));

            var (tempDirectory, tempFilePath) = await CreatePrivateTempFileAsync(cancellationToken);

            try
            {
                var remoteFilePath = BuildRemoteFilePath(remotePath, fileName);

                // Handle different write modes
                if (writeMode == FileWriteMode.CreateNew)
                {
                    // Check if file exists first
                    try
                    {
                        var checkCommand = $"ls {SmbClientCommandBuilder.QuotePath(remoteFilePath, nameof(fileName))}";
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
                    var existingTempFile = Path.Combine(tempDirectory, $"smbsharp_{Guid.NewGuid():N}.tmp");
                    try
                    {
                        var getCommand = $"get {SmbClientCommandBuilder.QuotePath(remoteFilePath, nameof(fileName))} {SmbClientCommandBuilder.QuotePath(existingTempFile, nameof(existingTempFile))}";
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
                var command = $"put {SmbClientCommandBuilder.QuotePath(tempFilePath, nameof(tempFilePath))} {SmbClientCommandBuilder.QuotePath(remoteFilePath, nameof(fileName))}";
                await ExecuteSmbClientCommandAsync(server, share, command, smbPath, cancellationToken);

                return true;
            }
            finally
            {
                DeleteTemporaryDirectory(tempDirectory);
            }
        }

        public async Task<bool> DeleteFileAsync(string smbPath, string fileName,
            CancellationToken cancellationToken = default)
        {
            SmbClientCommandBuilder.ValidateFileName(fileName, nameof(fileName));
            // Parse SMB path: //server/share/path or \\server\share\path
            var (server, share, remotePath) = ParseSmbPath(smbPath);

            // Delete file using smbclient
            var remoteFilePath = BuildRemoteFilePath(remotePath, fileName);

            var command = $"del {SmbClientCommandBuilder.QuotePath(remoteFilePath, nameof(fileName))}";
            try
            {
                await ExecuteSmbClientCommandAsync(server, share, command, smbPath, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                // DeleteFileAsync is intentionally idempotent and matches the native-path contract.
            }

            return true;
        }

        public async Task RenameFileAsync(string sourceDirectory, string sourceFileName, string destinationDirectory,
            string destinationFileName, CancellationToken cancellationToken)
        {
            SmbClientCommandBuilder.ValidateFileName(sourceFileName, nameof(sourceFileName));
            SmbClientCommandBuilder.ValidateFileName(destinationFileName, nameof(destinationFileName));
            var (sourceServer, sourceShare, sourcePath) = ParseSmbPath(sourceDirectory);
            var (destinationServer, destinationShare, destinationPath) = ParseSmbPath(destinationDirectory);
            if (!sourceServer.Equals(destinationServer, StringComparison.OrdinalIgnoreCase) ||
                !sourceShare.Equals(destinationShare, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Server-side rename requires both paths to use the same SMB share.");

            var source = BuildRemoteFilePath(sourcePath, sourceFileName);
            var destination = BuildRemoteFilePath(destinationPath, destinationFileName);
            var output = await ExecuteSmbClientCommandAsync(sourceServer, sourceShare,
                $"rename {SmbClientCommandBuilder.QuotePath(source, nameof(sourceFileName))} {SmbClientCommandBuilder.QuotePath(destination, nameof(destinationFileName))}",
                sourceDirectory, cancellationToken);

            if (output.Contains("NT_STATUS_OBJECT_NAME_COLLISION", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("NT_STATUS_OBJECT_NAME_EXISTS", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"The destination already exists on {sourceDirectory}: {output}");
            }
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
                var checkCommand = $"ls {SmbClientCommandBuilder.QuotePath(remotePath, nameof(smbPath))}";
                await ExecuteSmbClientCommandAsync(server, share, checkCommand, smbPath, cancellationToken);
                // If we reach here, the directory exists - return true (idempotent behavior)
                return true;
            }
            catch (FileNotFoundException)
            {
                // Directory doesn't exist, proceed to create it
            }

            var command = $"mkdir {SmbClientCommandBuilder.QuotePath(remotePath, nameof(smbPath))}";
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

            var argumentList = new List<string>();

            // When using WSL, prepend "smbclient" as the first argument (wsl will be the executable)
            if (_useWsl)
            {
                AddWslDistribution(argumentList);
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
                // Skip smbclient's default Kerberos-first attempt (see SmbClientSession for details).
                argumentList.Add("--use-kerberos=off");

                // PASSWD is supplied only to the child process environment; it is never put in argv or a file.
                var username = string.IsNullOrEmpty(_domain)
                    ? _username ?? string.Empty
                    : $"{_domain}\\{_username}";
                argumentList.Add("-U");
                argumentList.Add(username);
            }

            // Add command (convert any Windows paths in the command for WSL)
            argumentList.Add("-c");
            argumentList.Add(_useWsl ? ConvertWindowsPathsInCommand(command) : command);

            var executable = _useWsl ? "wsl" : "smbclient";
            IDictionary<string, string>? environmentVariables = null;
            if (!_useKerberos)
            {
                environmentVariables = new Dictionary<string, string> { ["PASSWD"] = _password ?? string.Empty };
                if (_useWsl)
                {
                    environmentVariables["TERM"] = "dumb";
                    AddWslEnvironment(environmentVariables);
                }
            }
            var result = await _processWrapper.ExecuteAsync(executable, argumentList, environmentVariables, cancellationToken);

            if (result.ExitCode == 0)
            {
                return result.StandardOutput;
            }

            SmbClientErrorClassifier.ThrowIfKnownError(
                $"{result.StandardOutput}\n{result.StandardError}", contextPath);
            throw new IOException($"Failed to execute smbclient command on {contextPath}: {result.StandardOutput} {result.StandardError}");
        }

        private static (string server, string share, string path) ParseSmbPath(string smbPath)
        {
            SmbClientCommandBuilder.ValidatePath(smbPath, nameof(smbPath));
            // Parse SMB path: //server/share/path or \\server\share\path
            var match = SmbPathRegexInstance.Match(smbPath);
            if (!match.Success)
            {
                throw new ArgumentException($"Invalid SMB path format: {smbPath}");
            }

            var server = match.Groups[1].Value;
            var share = match.Groups[2].Value;
            var path = match.Groups[3].Success ? match.Groups[3].Value.Replace('\\', '/') : "";
            SmbClientCommandBuilder.ValidatePath(server, nameof(smbPath));
            SmbClientCommandBuilder.ValidatePath(share, nameof(smbPath));
            if (!string.IsNullOrEmpty(path))
                SmbClientCommandBuilder.ValidatePath(path, nameof(smbPath));

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
                // directory and silently find nothing. Resolve the path relative to the share root;
                // smbclient does not treat a leading slash as a valid absolute path for ls.
                var command = string.IsNullOrEmpty(path)
                    ? "ls"
                    : $"ls {SmbClientCommandBuilder.QuotePath(path, nameof(directoryPath))}";
                await ExecuteSmbClientCommandAsync(server, share, command, directoryPath, cancellationToken);

                return true;
            }
            catch (Exception ex)
            {
                // CanConnectAsync intentionally reports connectivity as a bool (callers, e.g. health
                // checks, only care about success/failure), but swallowing the exception entirely left
                // no diagnostic trail when this fails in production. Log it so the real cause (auth
                // failure, timeout, broken session, etc.) is visible without changing the return contract.
                _logger.LogWarning(ex, "SMB connectivity check failed for {directoryPath}", directoryPath);
                return false;
            }
        }

        private static string ConvertToWslPath(string windowsPath) =>
            SmbClientPathUtil.ConvertToWslPath(windowsPath);

        private static string BuildRemoteFilePath(string remotePath, string fileName) =>
            string.IsNullOrEmpty(remotePath) ? fileName : $"{remotePath}/{fileName}";

        private async Task<(string directory, string filePath)> CreatePrivateTempFileAsync(
            CancellationToken cancellationToken)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"smbsharp_{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
#if NET7_0_OR_GREATER
                    File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                        UnixFileMode.UserExecute);
#else
                    var result = await _processWrapper.ExecuteAsync("chmod",
                        new[] { "700", directory }, null, cancellationToken);
                    if (result.ExitCode != 0)
                        throw new IOException($"Unable to restrict permissions on temporary SMB directory: {result.StandardError}");
#endif
                }
                return (directory, Path.Combine(directory, $"smbsharp_{Guid.NewGuid():N}.tmp"));
            }
            catch
            {
                DeleteTemporaryDirectory(directory);
                throw;
            }
        }

        private static void DeleteTemporaryDirectory(string directory)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
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

        private void AddWslDistribution(ICollection<string> arguments)
        {
            if (string.IsNullOrWhiteSpace(_wslDistribution))
                return;
            arguments.Add("-d");
            arguments.Add(_wslDistribution);
        }

        private static string ConvertWindowsPathsInCommand(string command) =>
            SmbClientPathUtil.ConvertWindowsPathsInCommand(command);
    }
}