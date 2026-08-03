using System.Text.RegularExpressions;

namespace SmbSharp.Business.SmbClient
{
    /// <summary>
    /// Shared path-conversion helpers used when building smbclient command lines, for both the
    /// one-shot (<see cref="SmbClientFileHandler"/>) and persistent-session execution paths.
    /// </summary>
    internal static class SmbClientPathUtil
    {
        /// <summary>
        /// Converts a Windows absolute path to a WSL path.
        /// Example: C:\Users\user\file.txt → /mnt/c/Users/user/file.txt
        /// </summary>
        public static string ConvertToWslPath(string windowsPath)
        {
            if (string.IsNullOrEmpty(windowsPath) || windowsPath.Length < 3)
                return windowsPath;

            // Match drive letter pattern like C:\ or C:/
            if (char.IsLetter(windowsPath[0]) && windowsPath[1] == ':' &&
                (windowsPath[2] == '\\' || windowsPath[2] == '/'))
            {
                var drive = char.ToLowerInvariant(windowsPath[0]);
                var rest = windowsPath.Substring(3).Replace('\\', '/');
                return $"/mnt/{drive}/{rest}";
            }

            return windowsPath;
        }

        /// <summary>
        /// Converts any Windows absolute paths found within a command string to WSL paths.
        /// Used for smbclient commands that contain local file paths (get/put operations).
        /// </summary>
        public static string ConvertWindowsPathsInCommand(string command)
        {
            // Match Windows absolute paths like C:\path or D:/path within the command
            return Regex.Replace(command, @"([A-Za-z]):([\\/])([^\s""]*)", match =>
            {
                var drive = char.ToLowerInvariant(match.Groups[1].Value[0]);
                var rest = match.Groups[3].Value.Replace('\\', '/');
                return $"/mnt/{drive}/{rest}";
            });
        }
    }
}
