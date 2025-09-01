using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Common.Math;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Questionable.Data;
using System;
using System.Runtime.InteropServices;

namespace Questionable.Controller;

internal sealed unsafe class InterruptHandler : IDisposable
{
    private readonly Hook<ProcessActionEffect> _processActionEffectHook;
    private readonly IClientState _clientState;
    private readonly TerritoryData _territoryData;
    private readonly ILogger<InterruptHandler> _logger;

    private delegate void ProcessActionEffect(uint sourceId, Character* sourceCharacter, Vector3* pos,
        EffectHeader* effectHeader, EffectEntry* effectArray, ulong* effectTail);

    public InterruptHandler(IGameInteropProvider gameInteropProvider, IClientState clientState,
        TerritoryData territoryData, ILogger<InterruptHandler> logger)
    {
        _clientState = clientState;
        _territoryData = territoryData;
        _logger = logger;
        _processActionEffectHook =
            gameInteropProvider.HookFromSignature<ProcessActionEffect>(Signatures.ActionEffect,
                HandleProcessActionEffect);
        _processActionEffectHook.Enable();
    }

    public event EventHandler? Interrupted;

    private void HandleProcessActionEffect(uint sourceId, Character* sourceCharacter, Vector3* pos,
        EffectHeader* effectHeader, EffectEntry* effectArray, ulong* effectTail)
    {
        try
        {
            if (!_territoryData.IsDutyInstance(_clientState.TerritoryType))
            {
                for (int i = 0; i < effectHeader->TargetCount; i++)
                {
                    uint targetId = (uint)(effectTail[i] & uint.MaxValue);
                    EffectEntry* effect = effectArray + 8 * i;

                    if (targetId == _clientState.LocalPlayer?.GameObjectId &&
                        effect->Type is EActionEffectType.Damage or EActionEffectType.BlockedDamage
                            or EActionEffectType.ParriedDamage)
                    {
                        _logger.LogTrace("Damage action effect on self, from {SourceId} ({EffectType})", sourceId,
                            effect->Type);
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

    public void Dispose()
    {
        _processActionEffectHook.Disable();
        _processActionEffectHook.Dispose();
    }

    private static class Signatures
    {
        internal const string ActionEffect = "40 ?? 56 57 41 ?? 41 ?? 41 ?? 48 ?? ?? ?? ?? ?? ?? ?? 48";
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct EffectEntry
    {
        [FieldOffset(0)] public EActionEffectType Type;
        [FieldOffset(1)] public byte Param0;
        [FieldOffset(2)] public byte Param1;
        [FieldOffset(3)] public byte Param2;
        [FieldOffset(4)] public byte Mult;
        [FieldOffset(5)] public byte Flags;
        [FieldOffset(6)] public ushort Value;

        public byte AttackType => (byte)(Param1 & 0xF);

        public override string ToString()
        {
            return
                $"Type: {Type}, p0: {Param0:D3}, p1: {Param1:D3}, p2: {Param2:D3} 0x{Param2:X2} '{Convert.ToString(Param2, 2).PadLeft(8, '0')}', mult: {Mult:D3}, flags: {Flags:D3} | {Convert.ToString(Flags, 2).PadLeft(8, '0')}, value: {Value:D6} ATTACK TYPE: {AttackType}";
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct EffectHeader
    {
        [FieldOffset(0)] public ulong AnimationTargetId;
        [FieldOffset(8)] public uint ActionID;
        [FieldOffset(12)] public uint GlobalEffectCounter;
        [FieldOffset(16)] public float AnimationLockTime;
        [FieldOffset(20)] public uint SomeTargetID;
        [FieldOffset(24)] public ushort SourceSequence;
        [FieldOffset(26)] public ushort Rotation;
        [FieldOffset(28)] public ushort AnimationId;
        [FieldOffset(30)] public byte Variation;
        [FieldOffset(31)] public ActionType ActionType;
        [FieldOffset(33)] public byte TargetCount;
    }

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
        Interrupt = 75,
    }
}
