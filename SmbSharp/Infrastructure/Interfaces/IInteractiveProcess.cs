using System.Text.RegularExpressions;

namespace SmbSharp.Infrastructure.Interfaces
{
    /// <summary>
    /// Abstraction over a long-lived, interactive external process (stdin/stdout piped),
    /// used to keep a single authenticated smbclient session alive across multiple commands.
    /// </summary>
    public interface IInteractiveProcess : IDisposable
    {
        /// <summary>
        /// True if the underlying process has exited (crashed, was killed, or the remote closed the connection).
        /// </summary>
        bool HasExited { get; }

        /// <summary>
        /// Starts the process with the given executable/arguments/environment.
        /// </summary>
        void Start(string fileName, IEnumerable<string> argumentList, IDictionary<string, string>? environmentVariables = null);

        /// <summary>
        /// Writes a line to the process's standard input.
        /// </summary>
        Task WriteLineAsync(string line, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads from standard output until the accumulated buffer's tail matches <paramref name="terminator"/>,
        /// or the process exits (EOF) before a match is found.
        /// </summary>
        /// <returns>The text read, with the matched terminator removed from the end.</returns>
        /// <exception cref="IOException">Thrown if the process exits (EOF) before the terminator is matched.</exception>
        Task<string> ReadUntilAsync(Regex terminator, CancellationToken cancellationToken = default);

        /// <summary>
        /// Attempts to terminate the process immediately.
        /// </summary>
        void Kill();
    }
}
