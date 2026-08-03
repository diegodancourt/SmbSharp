namespace SmbSharp.Business.SmbClient.Session
{
    /// <summary>
    /// Applies smbclient's known text-based error signatures (there's no per-command exit code in
    /// interactive mode) and throws the equivalent typed exception used by the rest of SmbSharp.
    /// </summary>
    internal static class SmbClientErrorClassifier
    {
        /// <summary>
        /// Inspects command output for smbclient's known error signatures and throws the matching
        /// exception type. Does nothing if no known error signature is found.
        /// </summary>
        public static void ThrowIfKnownError(string output, string contextPath)
        {
            var errorLower = output.ToLowerInvariant();

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
                errorLower.Contains("nt_status_logon_failure") ||
                errorLower.Contains("logon failure"))
            {
                throw new UnauthorizedAccessException($"Access denied to {contextPath}: {output}");
            }

            if (errorLower.Contains("bad network path") ||
                errorLower.Contains("network name not found") ||
                errorLower.Contains("nt_status_bad_network_name"))
            {
                throw new DirectoryNotFoundException($"The network path was not found: {contextPath}");
            }
        }
    }
}
