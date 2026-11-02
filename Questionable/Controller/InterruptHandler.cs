using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using JetBrains.Annotations;
namespace Questionable.Controller;

[RegisterSingleton]
internal sealed unsafe class InterruptHandler : IDisposable
{
    private readonly IClientState _clientState;
    private readonly IFramework _framework;
    private readonly ILogger<InterruptHandler> _logger;
    private readonly IObjectTable _objectTable;
    private readonly Hook<ProcessActionEffect> _processActionEffectHook;
    private readonly TerritoryData _territoryData;

    public InterruptHandler(IGameInteropProvider gameInteropProvider, IClientState clientState,
        IObjectTable objectTable, TerritoryData territoryData, IFramework framework, ILogger<InterruptHandler> logger)
    {
        _clientState = clientState;
        _objectTable = objectTable;
        _territoryData = territoryData;
        _framework = framework;
        _logger = logger;
        _processActionEffectHook =
            gameInteropProvider.HookFromAddress<ProcessActionEffect>(ActionEffectHandler.Addresses.Receive.Value,
                HandleProcessActionEffect);
        _processActionEffectHook.Enable();
    }

    public void Dispose()
    {
        IpcInvoke.TryOnFrameworkThread(_framework, () =>
        {
            _processActionEffectHook.Disable();
            _processActionEffectHook.Dispose();
        }, _logger);
    }

    public event EventHandler? Interrupted;

    /// <summary>
    ///     When the server last confirmed an action the local player started (auto-attacks excluded).
    /// </summary>
    public DateTime LastPlayerActionEffect { get; private set; } = DateTime.MinValue;

    private void HandleProcessActionEffect(uint sourceId, Character* sourceCharacter, Vector3* pos,
        ActionEffectHandler.Header* effectHeader, ActionEffectHandler.Effect* effectArray, ulong* effectTail)
    {
        try
        {
            // a non-zero source sequence means the client requested this action (rather than e.g. an auto-attack)
            if (sourceId == _objectTable[0]?.EntityId && effectHeader->SourceSequence != 0)
            {
                _logger.LogDebug("Action effect for own action {ActionType} {ActionId}", (ActionType)effectHeader->ActionType,
                    effectHeader->ActionId);
                LastPlayerActionEffect = DateTime.Now;
            }

            if (!_territoryData.IsDutyInstance(_clientState.TerritoryType))
            {
                for (int i = 0; i < effectHeader->NumTargets; i++)
                {
                    uint targetId = (uint)(effectTail[i] & uint.MaxValue);
                    // Eight effect slots per target.
                    ActionEffectHandler.Effect* effect = effectArray + 8 * i;

                    if (targetId == _objectTable[0]?.GameObjectId &&
                        (EActionEffectType)effect->Type is EActionEffectType.Damage or EActionEffectType.BlockedDamage
                            or EActionEffectType.ParriedDamage)
                    {
                        _logger.LogTrace("Damage action effect on self, from {SourceId} ({EffectType})", sourceId,
                            (EActionEffectType)effect->Type);
                        Interrupted?.Invoke(this, EventArgs.Empty);
                        break;
                    }
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Unable to process action effect");
        }
        finally
        {
            _processActionEffectHook.Original(sourceId, sourceCharacter, pos, effectHeader, effectArray, effectTail);
        }
    }

    private delegate void ProcessActionEffect(uint sourceId, Character* sourceCharacter, Vector3* pos,
        ActionEffectHandler.Header* effectHeader, ActionEffectHandler.Effect* effectArray, ulong* effectTail);

    [UsedImplicitly(ImplicitUseTargetFlags.Members)]
    private enum EActionEffectType : byte
    {
        None = 0,
        Miss = 1,
        FullResist = 2,
        Damage = 3,
        Heal = 4,
        BlockedDamage = 5,
        ParriedDamage = 6,
        Invulnerable = 7,
        NoEffectText = 8,
        Unknown0 = 9,
        MpLoss = 10,
        MpGain = 11,
        TpLoss = 12,
        TpGain = 13,
        ApplyStatusEffectTarget = 14,
        ApplyStatusEffectSource = 15,
        RecoveredFromStatusEffect = 16,
        LoseStatusEffectTarget = 17,
        LoseStatusEffectSource = 18,
        StatusNoEffect = 20,
        ThreatPosition = 24,
        EnmityAmountUp = 25,
        EnmityAmountDown = 26,
        StartActionCombo = 27,
        ComboSucceed = 28,
        Retaliation = 29,
        Knockback = 32,
        Attract1 = 33, //Here is an issue bout knockback. some is 32 some is 33.
        Attract2 = 34,
        Mount = 40,
        FullResistStatus = 52,
        FullResistStatus2 = 55,
        VFX = 59,
        Gauge = 60,
        JobGauge = 61,
        SetModelState = 72,
        SetHP = 73,
        PartialInvulnerable = 74,
        Interrupt = 75
    }
}
