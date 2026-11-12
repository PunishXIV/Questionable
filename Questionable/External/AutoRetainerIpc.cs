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

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);

    // A local copy of the few fields needed; Dalamud converts AutoRetainer's own type by name.
    internal sealed class Character
    {
        [JsonProperty("CID")] public ulong ContentId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string World { get; set; } = string.Empty;
    }
}
