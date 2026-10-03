using System.Text.RegularExpressions;

namespace SmbSharp.Business.SmbClient.Session
{
    /// <summary>
    /// Applies smbclient's known text-based error signatures (there's no per-command exit code in
    /// interactive mode) and throws the equivalent typed exception used by the rest of SmbSharp.
    /// </summary>
    internal static class SmbClientErrorClassifier
    {
        private static readonly Regex StatusLineRegex =
            new(@"(?:^\s*|(?:failed|error|status)\s+|:\s*)(NT_STATUS_[A-Z0-9_]+)(?:\s|$)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Inspects command output for smbclient's known error signatures and throws the matching
        /// exception type. Does nothing if no known error signature is found.
        /// </summary>
        public static void ThrowIfKnownError(string output, string contextPath)
        {
            var statusCodes = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r'))
                .Where(line => !Regex.IsMatch(line, @"^\s{2}.+\s{2,}[A-Z]+\s+\d+"))
                .Select(line => StatusLineRegex.Match(line))
                .Where(match => match.Success)
                .Select(match => match.Groups[1].Value.ToUpperInvariant())
                .ToHashSet(StringComparer.Ordinal);

            if (statusCodes.Overlaps(new[]
                {
                    "NT_STATUS_OBJECT_NAME_NOT_FOUND", "NT_STATUS_OBJECT_PATH_NOT_FOUND", "NT_STATUS_NO_SUCH_FILE"
                }))
            {
                throw new FileNotFoundException($"The specified path was not found on {contextPath}: {output}", contextPath);
            }

            if (statusCodes.Overlaps(new[] { "NT_STATUS_ACCESS_DENIED", "NT_STATUS_LOGON_FAILURE" }))
            {
                throw new UnauthorizedAccessException($"Access denied to {contextPath}: {output}");
            }

            if (statusCodes.Contains("NT_STATUS_BAD_NETWORK_NAME"))
            {
                throw new DirectoryNotFoundException($"The network path was not found: {contextPath}: {output}");
            }

        }
    }
}
