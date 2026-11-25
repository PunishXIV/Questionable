using Newtonsoft.Json;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class AutoRetainerIpc : Ipc, IDisposable
{
    private readonly ILogger<AutoRetainerIpc> _logger;
    private readonly EzIPCDisposalToken[] _disposalTokens;

    [EzIPC("GetRegisteredCIDs")] private readonly Func<List<ulong>> _getCharacterIds = null!;
    [EzIPC("GetOfflineCharacterData")] private readonly Func<ulong, Character?> _getCharacter = null!;
    [EzIPC("PluginState.EnableSingleMultiMode")] private readonly Action<EPassType?> _startPass = null!;
    [EzIPC("PluginState.GetMultiModeStatus")] private readonly Func<bool> _multiModeOn = null!;
    [EzIPC("PluginState.IsBusy")] private readonly Func<bool> _isBusy = null!;
    [EzIPC("PluginState.AbortAllTasks")] private readonly Action _abort = null!;
    [EzIPC("SetMultiModeEnabled")] private readonly Action<bool> _setMultiMode = null!;
    [EzIPC("PluginState.GetClosestRetainerVentureSecondsRemaining")] private readonly Func<ulong, long?> _ventureSeconds = null!;
    [EzIPC("PluginState.AreAnyEnabledVesselsReady")] private readonly Func<ulong, bool?> _vesselsReady = null!;

    // AutoRetainer's MultiModeType, only for the call's signature.
    internal enum EPassType
    {
        Retainers,
        Submersibles,
        Everything,
    }

    // Index of Questionable in AutoRetainer's ActivePlugin.
    private const int QuestionableSlot = 2;

    internal Func<bool>? KeepEnabled { get; set; }

    public AutoRetainerIpc(ILogger<AutoRetainerIpc> logger)
    {
        _logger = logger;
        _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);
    }

    public override string InternalName => "AutoRetainer";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => GetVersion() != null;

    public IReadOnlyList<Character> GetCharacters() => IpcInvoke.SafeFunc<IReadOnlyList<Character>>(
        () => _getCharacterIds()
            .Select(id => _getCharacter(id))
            .Where(x => x is { ContentId: not 0, Name.Length: > 0 })
            .Select(x => x!)
            .ToList(),
        [], _logger, "Unable to read AutoRetainer's character list");

    public bool IsMultiModeOn => IpcInvoke.SafeFunc(() => _multiModeOn(), fallback: false);

    public bool IsBusy => IpcInvoke.SafeFunc(() => _isBusy(), fallback: false);

    // No type: AutoRetainer uses its own multi-mode setting.
    public bool StartPass(EPassType? type)
    {
        _logger.LogInformation("Asking AutoRetainer for one multi-mode pass ({Type})", type?.ToString() ?? "its own setting");
        return IpcInvoke.SafeFunc(() =>
        {
            _startPass(type);
            return true;
        }, false, _logger, "Unable to start AutoRetainer's multi-mode");
    }

    public void StopPass()
    {
        _logger.LogInformation("Stopping AutoRetainer's multi-mode");
        IpcInvoke.SafeFunc(() =>
        {
            _abort();
            _setMultiMode(false);
            return true;
        }, false, _logger, "Unable to stop AutoRetainer's multi-mode");
    }

    public bool RetainersReady(ulong contentId) =>
        IpcInvoke.SafeFunc(() => _ventureSeconds(contentId) is <= 0, fallback: false);

    public bool VesselsReady(ulong contentId) =>
        IpcInvoke.SafeFunc(() => _vesselsReady(contentId) == true, fallback: false);

    [EzIPCEvent("PluginState.OnBeforePluginDisable")]
    private void OnBeforePluginDisable(bool[] list)
    {
        if (KeepEnabled?.Invoke() == true && list.Length > QuestionableSlot)
            list[QuestionableSlot] = false;
    }

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);

    // A local copy of the few fields needed; Dalamud converts AutoRetainer's own type by name.
    internal sealed class Character
    {
        [JsonProperty("CID")] public ulong ContentId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string World { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public bool WorkshopEnabled { get; set; }
    }
}
