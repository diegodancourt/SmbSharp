namespace SmbSharp.Infrastructure.Interfaces
{
    /// <summary>
    /// Creates new <see cref="IInteractiveProcess"/> instances. Split out as a factory (rather than
    /// instantiating IInteractiveProcess directly) so session creation remains mockable in tests.
    /// </summary>
    public interface IInteractiveProcessFactory
    {
        /// <summary>
        /// Creates a new, unstarted interactive process instance.
        /// </summary>
        IInteractiveProcess Create();
    }
}
