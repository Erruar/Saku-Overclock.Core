namespace Saku_Overclock.Core.Contracts;

public interface ISafeGuardsService
{
    void EnsureInitialized();
    bool PreviousSessionCrashed { get; }
    event EventHandler? CrashDetected;
}