using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SmbSharp.Business.Interfaces;
using SmbSharp.Enums;
using SmbSharp.Infrastructure.Interfaces;

namespace SmbSharp.Business.SmbClient
{
    internal class SmbClientFileHandler : ISmbClientFileHandler
    {
        private readonly ILogger<SmbClientFileHandler> _logger;
        private readonly IProcessWrapper _processWrapper;
        private readonly bool _useKerberos;
        private readonly string? _username;
        private readonly string? _password;
        private readonly string? _domain;

        private static readonly Regex SmbPathRegexInstance =
            new(@"^[/\\]{2}([^/\\]+)[/\\]([^/\\]+)(?:[/\\](.*))?$", RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegexInstance = new(@"\s+", RegexOptions.Compiled);

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
                    // ExecuteAsync is async but we need sync for this method
                    // Use Task.Run to execute synchronously
                    var result = Task.Run(() => _processWrapper.ExecuteAsync("smbclient", "--version")).Result;
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
        
        public SmbClientFileHandler(ILogger<SmbClientFileHandler> logger, IProcessWrapper processWrapper, bool useKerberos, string? username = null, string? password = null,
            string? domain = null)
        {
            _logger = logger;
            _processWrapper = processWrapper ?? throw new ArgumentNullException(nameof(processWrapper));
            _useKerberos = useKerberos;
            if(!useKerberos && (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)))
            {
                _logger.LogError("Username and Password must be provided when not using Kerberos authentication.");
                throw new ArgumentException("Username and Password must be provided when not using Kerberos authentication.");
            }

            _username = username;
            _password = password;
            _domain = domain;
        }

        public async Task<IEnumerable<string>> EnumerateFilesAsync(string smbPath,
            CancellationToken cancellationToken = default)
        {
            var files = new List<string>();
            try
            {
                // Parse SMB path: //server/share/path or \\server\share\path
                var (server, share, path) = ParseSmbPath(smbPath);

                var command = string.IsNullOrEmpty(path) ? "ls" : $"ls {path}/*";
                var output = await ExecuteSmbClientCommandAsync(server, share, command, smbPath, cancellationToken);

                // Parse smbclient output - format is typically:
                // filename    A    size  date
                
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    // Skip directories (marked with 'D'), current/parent dir entries, and header/footer lines
                    if (line.Contains("blocks of size") || line.Contains("blocks available") ||
                        line.Trim().StartsWith('.') || string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var parts = WhitespaceRegexInstance.Split(line.Trim());
                    if (parts.Length < 2)
                    {
                        continue;
                    }

                    var fileName = parts[0];
                    var attributes = parts[1];

                    // Only include files (not directories marked with 'D')
                    if (attributes.Contains('A') && !attributes.Contains('D') && !string.IsNullOrWhiteSpace(fileName))
                    {
                        files.Add(fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enumerating files in SMB path: {SmbPath}", smbPath);
                throw;
            }

            return files;
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
            // Note: No shell escaping needed since UseShellExecute=false in ProcessWrapper
            // ProcessStartInfo passes arguments directly to the process without shell interpretation
            // However, we still need to escape quotes within the smbclient -c command parameter
            var escapedCommand = command.Replace("\\", "\\\\").Replace("\"", "\\\"");

            string arguments;
            Dictionary<string, string>? environmentVariables = null;

            if (_useKerberos)
            {
                // Use Kerberos authentication (kinit ticket). Modern smbclient versions use --use-kerberos
                arguments =
                    $"//{server}/{share} --use-kerberos=required -c \"{escapedCommand}\"";
            }
            else
            {
                // Use username/password authentication
                var userArg = string.IsNullOrEmpty(_domain)
                    ? _username ?? string.Empty
                    : $"{_domain}\\{_username}";

                // SECURITY: Pass password via environment variable instead of command line
                // This prevents password from being visible in process listings
                // Explicitly disable Kerberos to force NTLM authentication (important for IP-based connections)
                arguments =
                    $"//{server}/{share} --use-kerberos=disabled -U \"{userArg}\" -c \"{escapedCommand}\"";
                environmentVariables = new Dictionary<string, string>
                {
                    ["PASSWD"] = _password ?? string.Empty
                };
            }

            var result = await _processWrapper.ExecuteAsync("smbclient", arguments, environmentVariables, cancellationToken);

            if (result.ExitCode == 0)
            {
                return result.StandardOutput;
            }

            // Try to differentiate error types based on smbclient error messages
            var errorLower = (result.StandardError ?? string.Empty).ToLowerInvariant();

            if (errorLower.Contains("does not exist") ||
                errorLower.Contains("not found") ||
                errorLower.Contains("nt_status_object_name_not_found"))
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

        /// <summary>
        /// Escapes a string for safe use as a shell argument by removing/escaping dangerous characters.
        /// </summary>
        private string EscapeShellArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
                return argument;

            // Remove or escape characters that could cause command injection
            // For smbclient paths, we need to be careful with quotes, backticks, dollar signs, etc.
            return argument
                .Replace("\\", "\\\\") // Escape backslashes first
                .Replace("\"", "\\\"") // Escape double quotes
                .Replace("`", "\\`") // Escape backticks
                .Replace("$", "\\$"); // Escape dollar signs
        }

        /// <summary>
        /// Escapes a string for safe use inside smbclient -c command string.
        /// </summary>
        private string EscapeCommandString(string command)
        {
            if (string.IsNullOrEmpty(command))
                return command;

            // Escape quotes and backslashes in the command string
            return command
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        private (string server, string share, string path) ParseSmbPath(string smbPath)
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

                // Try to list files to test connection - if path is specified, check that specific directory
                var command = string.IsNullOrEmpty(path) ? "ls" : $"cd \"{path}\"; ls";
                await ExecuteSmbClientCommandAsync(server, share, command, directoryPath, cancellationToken);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}