namespace SmbSharp.Business.SmbClient
{
    internal static class SmbClientCommandBuilder
    {
        public static string QuotePath(string path, string parameterName)
        {
            ValidatePath(path, parameterName);
            return $"\"{path}\"";
        }

        public static void ValidatePath(string path, string parameterName)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("SMB path cannot be empty.", parameterName);

            if (path.Any(c => char.IsControl(c) || c is '"' or ';' or '*' or '?'))
                throw new ArgumentException("SMB paths cannot contain control characters, quotes, command separators, or wildcards.", parameterName);

            if (path.TrimStart('/').TrimStart('\\').StartsWith('!'))
                throw new ArgumentException("SMB paths cannot begin with a local shell command marker.", parameterName);
        }

        public static void ValidateFileName(string fileName, string parameterName)
        {
            ValidatePath(fileName, parameterName);
            if (fileName.Contains('/') || fileName.Contains('\\'))
                throw new ArgumentException("A file name cannot contain path separators.", parameterName);
        }
    }
}
