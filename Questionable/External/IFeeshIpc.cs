namespace Questionable.External;

internal interface IFeeshIpc
{
    /// <summary>WahTools is loaded and Feesh answers.</summary>
    bool IsAvailable();

    /// <summary><c>WahTools.IsBusy</c>; null if unreadable.</summary>
    bool? GetBusy();

    /// <summary>Feesh knows a spot for this fish.</summary>
    bool CanCatch(uint fishId);

    /// <summary>Catches <paramref name="additionalCount"/> more and keeps them until released.</summary>
    FeeshStartResult StartCatch(uint fishId, int additionalCount, out string message);

    void PollOperation();

    /// <summary>Ends the job early; the executor keeps ownership.</summary>
    void RequestStop();

    void CancelOwned();
    void ObserveIdle();

    /// <summary>Lets Feesh use this fish again.</summary>
    void ReleaseCatch(uint fishId);
}
