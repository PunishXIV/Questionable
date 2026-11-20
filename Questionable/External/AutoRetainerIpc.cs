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
    [EzIPC("PluginState.CanAutoLogin")] private readonly Func<bool> _canRelog = null!;
    [EzIPC("PluginState.Relog")] private readonly Func<string, bool> _relog = null!;
    [EzIPC("PluginState.EnableSingleMultiMode")] private readonly Action<EPassType?> _startPass = null!;
    [EzIPC("PluginState.GetMultiModeStatus")] private readonly Func<bool> _multiModeOn = null!;
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

    // True at the title screen, or in game while the character is free to act.
    public bool CanRelog => IpcInvoke.SafeFunc(() => _canRelog(), fallback: false);

    public IReadOnlyList<Character> GetCharacters() => IpcInvoke.SafeFunc<IReadOnlyList<Character>>(
        () => _getCharacterIds()
            .Select(id => _getCharacter(id))
            .Where(x => x is { ContentId: not 0, Name.Length: > 0 })
            .Select(x => x!)
            .ToList(),
        [], _logger, "Unable to read AutoRetainer's character list");

    public bool Relog(Character character)
    {
        _logger.LogInformation("Asking AutoRetainer to relog to {Name}@{World}", character.Name, character.World);
        return IpcInvoke.SafeFunc(() => _relog($"{character.Name}@{character.World}"), false, _logger,
            "Unable to ask AutoRetainer for a relog");
    }

    public bool IsMultiModeOn => IpcInvoke.SafeFunc(() => _multiModeOn(), fallback: false);

    // No type: AutoRetainer uses its own multi-mode setting.
    public bool StartPass()
    {
        _logger.LogInformation("Asking AutoRetainer for one multi-mode pass");
        return IpcInvoke.SafeFunc(() =>
        {
            _startPass(null);
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
