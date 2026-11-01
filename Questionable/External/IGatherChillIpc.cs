namespace Questionable.External;

internal interface IGatherChillIpc
{
    bool IsAvailable();
    bool IsReady();
    bool? GetBusy();
    bool CanGather(uint itemId);
    GatherStartResult StartRequest(uint itemId, int additionalCount, out string message);
    void PollOperation();
    void CancelOwned();
    void ObserveIdle();
}
