using FFXIVClientStructs.FFXIV.Client.UI.Info;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class MogmailIpc : Ipc, IDisposable
{
    private readonly ILogger<MogmailIpc> _logger;
    private readonly EzIPCDisposalToken[] _disposalTokens;

    [EzIPC("IsAvailable")] private readonly Func<bool> _isAvailable = null!;
    [EzIPC("IsBusy")] private readonly Func<bool> _isBusy = null!;
    [EzIPC("IsMailboxOpen")] private readonly Func<bool> _isMailboxOpen = null!;
    [EzIPC("ClaimAndDelete")] private readonly Func<bool> _claimAndDelete = null!;
    [EzIPC("Stop")] private readonly Func<bool> _stop = null!;

    public MogmailIpc(ILogger<MogmailIpc> logger)
    {
        _logger = logger;
        _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);
    }

    public override string InternalName => "Mogmail";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() => GetVersion() != null && !_isBusy(), fallback: false);

    public bool IsInstalled =>
        IPCSubscriber.IsInstalled(InternalName) && IpcInvoke.SafeFunc(() => _isAvailable(), fallback: false);

    // A claim, read or pop run is in flight; Auto Pop after a claim counts too.
    public bool IsBusy => IpcInvoke.SafeFunc(() => _isBusy(), fallback: false);

    public bool IsMailboxOpen => IpcInvoke.SafeFunc(() => _isMailboxOpen(), fallback: false);

    // Mog Station and reward letters; the game counts these apart from player mail, without opening the mailbox.
    public static unsafe uint? RewardLetters
    {
        get
        {
            InfoProxyLetter* letters = InfoProxyLetter.Instance();
            return letters != null ? letters->NumLettersFromPurchases : null;
        }
    }

    // False when Mogmail refused: busy, mailbox closed, or nothing to claim.
    public bool ClaimAndDelete() =>
        IpcInvoke.SafeFunc(() => _claimAndDelete(), fallback: false, _logger, "Could not call Mogmail.ClaimAndDelete");

    // Aborts Mogmail's claim, read and pop runs; true if anything was running.
    public bool Stop() => IpcInvoke.SafeFunc(() => _stop(), fallback: false, _logger, "Could not call Mogmail.Stop");

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);
}
