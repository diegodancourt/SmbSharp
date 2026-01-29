using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SmbSharp.Business.SmbClient
{
    internal static class SmbClientFileHandler
    {
        private static readonly Regex SmbPathRegexInstance = new(@"^[/\\]{2}([^/\\]+)[/\\]([^/\\]+)(?:[/\\](.*))?$", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegexInstance = new(@"\s+", RegexOptions.Compiled);

        public static async Task<IEnumerable<string>> EnumerateFilesAsync(string smbPath, bool useKerberos,
            string? username, string? password, string? domain, CancellationToken cancellationToken = default)
        {
            // Parse SMB path: //server/share/path or \\server\share\path
            var (server, share, path) = ParseSmbPath(smbPath);

            var command = string.IsNullOrEmpty(path) ? "ls" : $"ls {path}/*";
            var output = await ExecuteSmbClientCommandAsync(server, share, command, smbPath, useKerberos, username,
                password, domain, cancellationToken);

            // Parse smbclient output - format is typically:
            // filename    A    size  date
            var files = new List<string>();
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

            return files;
        }

        private static async Task<string> ExecuteSmbClientCommandAsync(string server, string share, string command,
            string contextPath, bool useKerberos, string? username, string? password, string? domain, CancellationToken cancellationToken = default)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "smbclient",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Escape server and share to prevent command injection
            var escapedServer = EscapeShellArgument(server);
            var escapedShare = EscapeShellArgument(share);

            if (useKerberos)
            {
                // Use Kerberos authentication (kinit ticket). Modern smbclient versions use --use-kerberos
                processStartInfo.Arguments = $"//{escapedServer}/{escapedShare} --use-kerberos=required -c \"{EscapeCommandString(command)}\"";
            }
            else
            {
                // Use username/password authentication
                var userArg = string.IsNullOrEmpty(domain)
                    ? username ?? string.Empty
                    : $"{domain}\\{username}";

                // SECURITY: Pass password via environment variable instead of command line
                // This prevents password from being visible in process listings
                processStartInfo.Arguments = $"//{escapedServer}/{escapedShare} -U \"{EscapeShellArgument(userArg)}\" -c \"{EscapeCommandString(command)}\"";
                processStartInfo.Environment["PASSWD"] = password ?? string.Empty;
            }

            using var process = new Process();
            process.StartInfo = processStartInfo;
            process.Start();

            // ReadToEndAsync with CancellationToken is only available in .NET 7+
#if NET7_0_OR_GREATER
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
#else
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
#endif

#if NETCOREAPP3_1
            // .NET Core 3.1 doesn't have WaitForExitAsync with cancellation token
            await Task.Run(() => process.WaitForExit(), cancellationToken);
#else
            await process.WaitForExitAsync(cancellationToken);
#endif

            if (process.ExitCode != 0)
            {
                // Try to differentiate error types based on smbclient error messages
                var errorLower = error.ToLowerInvariant();

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
                        $"Access denied to {contextPath}: {error}");
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
                    $"Failed to execute smbclient command on {contextPath}: {error}");
            }

            return output;
        }

        /// <summary>
        /// Escapes a string for safe use as a shell argument by removing/escaping dangerous characters.
        /// </summary>
        private static string EscapeShellArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
                return argument;

            // Remove or escape characters that could cause command injection
            // For smbclient paths, we need to be careful with quotes, backticks, dollar signs, etc.
            return argument
                .Replace("\\", "\\\\")  // Escape backslashes first
                .Replace("\"", "\\\"")  // Escape double quotes
                .Replace("`", "\\`")    // Escape backticks
                .Replace("$", "\\$");   // Escape dollar signs
        }

        /// <summary>
        /// Escapes a string for safe use inside smbclient -c command string.
        /// </summary>
        private static string EscapeCommandString(string command)
        {
            if (string.IsNullOrEmpty(command))
                return command;

            // Escape quotes and backslashes in the command string
            return command
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
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

        public static async Task<Stream> GetFileStreamAsync(string smbPath, string fileName, bool useKerberos,
            string? username, string? password, string? domain, CancellationToken cancellationToken = default)
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
            await ExecuteSmbClientCommandAsync(server, share, command, smbPath, useKerberos, username, password, domain, cancellationToken);

            if (!File.Exists(tempFilePath))
            {
                throw new FileNotFoundException(
                    $"Failed to download file {fileName} from {smbPath}");
            }

            // Return a FileStream with DeleteOnClose option to auto-cleanup temp file
            return new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.None, 4096,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        }

        public static async Task<bool> WriteFileAsync(string smbPath, string fileName, Stream stream, bool useKerberos,
            string? username, string? password, string? domain, CancellationToken cancellationToken = default)
        {
            return await WriteFileAsync(smbPath, fileName, stream, Enums.FileWriteMode.Overwrite, useKerberos, username, password, domain, cancellationToken);
        }

        public static async Task<bool> WriteFileAsync(string smbPath, string fileName, Stream stream, Enums.FileWriteMode writeMode, bool useKerberos,
            string? username, string? password, string? domain, CancellationToken cancellationToken = default)
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
                if (writeMode == Enums.FileWriteMode.CreateNew)
                {
                    // Check if file exists first
                    try
                    {
                        var checkCommand = $"ls \"{remoteFilePath}\"";
                        await ExecuteSmbClientCommandAsync(server, share, checkCommand, smbPath, useKerberos, username, password, domain, cancellationToken);
                        // If we get here, file exists
                        throw new IOException($"File already exists: {smbPath}/{fileName}");
                    }
                    catch (FileNotFoundException)
                    {
                        // Good - file doesn't exist, continue
                    }
                }
                else if (writeMode == Enums.FileWriteMode.Append)
                {
                    // For append mode, download existing file first if it exists
                    try
                    {
                        var existingTempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_existing_{fileName}");
                        var getCommand = $"get \"{remoteFilePath}\" \"{existingTempFile}\"";
                        await ExecuteSmbClientCommandAsync(server, share, getCommand, smbPath, useKerberos, username, password, domain, cancellationToken);

                        // Copy existing file to temp file, then append new content
                        await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                        {
                            await using (var existingStream = new FileStream(existingTempFile, FileMode.Open, FileAccess.Read))
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
                        await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                        {
                            await stream.CopyToAsync(fileStream, cancellationToken);
                        }
                    }
                }
                else // Overwrite
                {
                    // Write stream to temp file
                    await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                    {
                        await stream.CopyToAsync(fileStream, cancellationToken);
                    }
                }

                // Upload file using smbclient
                var command = $"put \"{tempFilePath}\" \"{remoteFilePath}\"";
                await ExecuteSmbClientCommandAsync(server, share, command, smbPath, useKerberos, username, password, domain, cancellationToken);

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

        public static async Task<bool> DeleteFileAsync(string smbPath, string fileName, bool useKerberos,
            string? username, string? password, string? domain, CancellationToken cancellationToken = default)
        {
            // Parse SMB path: //server/share/path or \\server\share\path
            var (server, share, remotePath) = ParseSmbPath(smbPath);

            // Delete file using smbclient
            var remoteFilePath = string.IsNullOrEmpty(remotePath)
                ? fileName
                : $"{remotePath}/{fileName}";

            var command = $"del \"{remoteFilePath}\"";
            await ExecuteSmbClientCommandAsync(server, share, command, smbPath, useKerberos, username, password, domain, cancellationToken);

            return true;
        }

        public static async Task<bool> CreateDirectoryAsync(string smbPath, bool useKerberos,
            string? username, string? password, string? domain, CancellationToken cancellationToken = default)
        {
            // Parse SMB path: //server/share/path or \\server\share\path
            var (server, share, remotePath) = ParseSmbPath(smbPath);

            if (string.IsNullOrEmpty(remotePath))
            {
                throw new ArgumentException("Directory path cannot be empty", nameof(smbPath));
            }

            var command = $"mkdir \"{remotePath}\"";
            await ExecuteSmbClientCommandAsync(server, share, command, smbPath, useKerberos, username, password, domain, cancellationToken);

            return true;
        }

        public static async Task<bool> CanConnectAsync(string smbPath, bool useKerberos,
            string? username, string? password, string? domain, CancellationToken cancellationToken = default)
        {
            try
            {
                // Parse SMB path: //server/share/path or \\server\share\path
                var (server, share, _) = ParseSmbPath(smbPath);

                // Try to list files to test connection
                var command = "ls";
                await ExecuteSmbClientCommandAsync(server, share, command, smbPath, useKerberos, username, password, domain, cancellationToken);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}