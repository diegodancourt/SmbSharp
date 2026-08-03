using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmbSharp.Infrastructure.Interfaces;

namespace SmbSharp.Infrastructure
{
    /// <summary>
    /// Default factory that creates real <see cref="InteractiveProcess"/> instances.
    /// </summary>
    [ExcludeFromCodeCoverage]
    internal class InteractiveProcessFactory : IInteractiveProcessFactory
    {
        private readonly ILoggerFactory? _loggerFactory;

        public InteractiveProcessFactory(ILoggerFactory? loggerFactory = null)
        {
            _loggerFactory = loggerFactory;
        }

        public IInteractiveProcess Create()
        {
            return new InteractiveProcess(_loggerFactory?.CreateLogger<InteractiveProcess>());
        }
    }
}
