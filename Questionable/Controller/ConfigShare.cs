using System.Runtime.InteropServices;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Questionable.Controller;

// Drives the game's Config Share window; adapted from Knightmore's draft, whose offsets and event ids are his reverse engineering.
[RegisterSingleton]
internal sealed unsafe class ConfigShare : IDisposable
{
    internal const uint AllCategories = 0x7F;
    private const int MainEvent = 1;
    private const int ShareCodeEvent = 4;
    private const int DownloadEvent = 5;
    private const int ConfirmationEvent = 6;
    private const int ConfirmDownload = 10;
    private const int DownloadSucceeded = 11;
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);

    private enum EPhase
    {
        Start,
        Main,
        ShareCode,
        Preview,
        Confirm,
        Result,
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x8C8)]
    private struct AgentState
    {
        [FieldOffset(0)] public AgentInterface Interface;
        [FieldOffset(0x28)] public ushort MainAddonId;
        [FieldOffset(0x2E)] public ushort ShareCodeAddonId;
        [FieldOffset(0x30)] public ushort DownloadAddonId;
        [FieldOffset(0x34)] public int State;
        [FieldOffset(0x38)] public ulong ContentId;
        [FieldOffset(0x7D0)] public uint CategoryMask;
        [FieldOffset(0x858)] public int ConfirmationType;
        [FieldOffset(0x85C)] public uint ConfirmationAddonId;
    }

    private readonly Configuration _configuration;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IFramework _framework;
    private readonly IObjectTable _objectTable;
    private readonly IChatGui _chatGui;
    private readonly ILogger<ConfigShare> _logger;
    private readonly HashSet<ulong> _tried = [];
    private (string Code, uint Categories)? _pending;
    private EPhase _phase;
    private DateTime _deadline;
    private ulong _target;
    private uint _confirmationAddonId;

    public ConfigShare(Configuration configuration, IDalamudPluginInterface pluginInterface, IFramework framework,
        IObjectTable objectTable, IChatGui chatGui, ILogger<ConfigShare> logger)
    {
        _configuration = configuration;
        _pluginInterface = pluginInterface;
        _framework = framework;
        _objectTable = objectTable;
        _chatGui = chatGui;
        _logger = logger;
        _framework.Update += Tick;
    }

    public string? Status { get; private set; }
    public bool IsBusy => _pending != null;

    internal static string[] CategoryLabels =>
    [
        _L("HUD layout"),
        _L("Shared macros"),
        _L("UI settings, controls, keybinds and log filters"),
        _L("Saved UI data"),
        _L("Hotbars"),
        _L("Character macros"),
        _L("System settings"),
    ];

    internal static bool IsValid(string code, uint categories) =>
        categories != 0 && (categories & ~AllCategories) == 0 && code.Length == 16 && code.All(char.IsAsciiLetterOrDigit);

    // Once per character and code: a list run shouldn't download the same settings again every time.
    internal static bool IsDue(Configuration.CharacterConfiguration settings, ulong contentId, bool tried) =>
        settings.ApplyShare && IsValid(settings.ShareCode, settings.ShareCategories) && !tried &&
        !(settings.ShareApplied.TryGetValue(contentId, out string? applied) && applied == settings.ShareCode);

    public bool Due(ulong contentId) => IsDue(_configuration.Characters, contentId, _tried.Contains(contentId));

    public bool Apply()
    {
        Configuration.CharacterConfiguration settings = _configuration.Characters;
        if (IsBusy || !IsValid(settings.ShareCode, settings.ShareCategories) || _objectTable[0] == null)
            return false;

        _tried.Add(PlayerState.Instance()->ContentId);
        _pending = (settings.ShareCode, settings.ShareCategories);
        _phase = EPhase.Start;
        Status = _L("Applying shared settings...");
        return true;
    }

    private void Tick(IFramework framework)
    {
        if (_pending is not { } request)
            return;

        ulong contentId = _objectTable[0] == null ? 0 : PlayerState.Instance()->ContentId;
        AgentModule* module = contentId == 0 ? null : AgentModule.Instance();
        AgentState* agent = module == null ? null : (AgentState*)module->GetAgentByInternalId(AgentId.ConfigShare);
        if (agent == null || (_phase != EPhase.Start && (contentId != _target || agent->ContentId != _target)))
        {
            Finish(_L("Shared settings not applied: the character changed or logged out."), success: false);
            return;
        }

        if (_phase != EPhase.Start && DateTime.Now >= _deadline)
        {
            Finish(_L("Shared settings not applied: the game's Config Share window didn't answer."), success: false);
            return;
        }

        if (agent->ConfirmationAddonId != 0 && _phase is not (EPhase.Confirm or EPhase.Result))
        {
            Finish(_L("Shared settings not applied: the game showed a Config Share message; read it in the game."), success: false);
            return;
        }

        switch (_phase)
        {
            case EPhase.Start:
                if (agent->State != 0)
                {
                    Finish(_L("Shared settings not applied: close the game's Config Share window first."), success: false);
                    return;
                }

                _target = contentId;
                agent->Interface.Show();
                if (agent->State == 0 || agent->ContentId != _target)
                {
                    Finish(_L("Shared settings not applied: the game couldn't open Config Share."), success: false);
                    return;
                }

                Advance(EPhase.Main);
                break;
            case EPhase.Main when agent->State == 2 && agent->MainAddonId != 0:
                Send(agent, MainEvent, 15);
                Advance(EPhase.ShareCode);
                break;
            case EPhase.ShareCode when agent->State == 8 && agent->ShareCodeAddonId != 0:
                SubmitCode(agent, request.Code);
                Advance(EPhase.Preview);
                break;
            case EPhase.Preview when agent->State == 10 && agent->DownloadAddonId != 0:
                agent->CategoryMask = request.Categories;
                Send(agent, DownloadEvent, 27);
                _confirmationAddonId = agent->ConfirmationAddonId;
                if (agent->ConfirmationType != ConfirmDownload || _confirmationAddonId == 0)
                {
                    Finish(_L("Shared settings not applied: the game didn't ask to confirm the download."), success: false);
                    return;
                }

                Advance(EPhase.Confirm);
                break;
            case EPhase.Confirm:
                if (agent->State != 10 || agent->ConfirmationType != ConfirmDownload ||
                    agent->ConfirmationAddonId != _confirmationAddonId || agent->CategoryMask != request.Categories)
                {
                    Finish(_L("Shared settings not applied: the download confirmation changed."), success: false);
                    return;
                }

                Send(agent, ConfirmationEvent, 0);
                Advance(EPhase.Result);
                break;
            case EPhase.Result when agent->ConfirmationAddonId != 0:
                if (agent->ConfirmationType != DownloadSucceeded)
                {
                    Finish(_L("Shared settings not applied: the game reported a download error; read its message."), success: false);
                    return;
                }

                _configuration.Characters.ShareApplied[_target] = request.Code;
                _pluginInterface.SavePluginConfig(_configuration);
                Close(agent);
                Finish(_L("Shared settings applied."), success: true);
                break;
        }
    }

    // The result dialog would otherwise sit over the next step of a character list.
    private static void Close(AgentState* agent)
    {
        AtkUnitBase* dialog = AddonUtils.GetAddonById(agent->ConfirmationAddonId);
        if (dialog != null)
            dialog->Close(false);
        agent->Interface.Hide();
    }

    private static void SubmitCode(AgentState* agent, string code)
    {
        Span<byte> utf8 = stackalloc byte[17];
        int length = Encoding.UTF8.GetBytes(code.AsSpan(), utf8);
        utf8[length] = 0;
        fixed (byte* text = utf8)
        {
            AtkValue* values = stackalloc AtkValue[2];
            values[0] = new AtkValue { Type = AtkValueType.Int, Int = 23 };
            values[1] = new AtkValue { Type = AtkValueType.String, String = text };
            Send(agent, ShareCodeEvent, values, 2);
        }

        Send(agent, ShareCodeEvent, 24);
    }

    private static void Send(AgentState* agent, ulong eventKind, int argument)
    {
        AtkValue value = new() { Type = AtkValueType.Int, Int = argument };
        Send(agent, eventKind, &value, 1);
    }

    private static void Send(AgentState* agent, ulong eventKind, AtkValue* values, uint count)
    {
        AtkValue result = default;
        agent->Interface.ReceiveEvent(&result, values, count, eventKind);
    }

    private void Advance(EPhase next)
    {
        _phase = next;
        _deadline = DateTime.Now + StepTimeout;
    }

    private void Finish(string message, bool success)
    {
        _logger.LogInformation("Config Share finished in {Phase}: {Message}", _phase, message);
        _phase = EPhase.Start;
        _pending = null;
        Status = message;
        if (success)
            _chatGui.Print(message, CommandHandler.MessageTag, CommandHandler.TagColor);
        else
            _chatGui.PrintError(message, CommandHandler.MessageTag, CommandHandler.TagColor);
    }

    public void Dispose() => _framework.Update -= Tick;
}
