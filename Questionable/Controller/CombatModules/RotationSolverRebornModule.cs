using System;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Questionable.External;
using Questionable.Model.Common;
namespace Questionable.Controller.CombatModules;

internal sealed class RotationSolverRebornModule(RotationSolverRebornIpc rotationSolverRebornIpc) : ICombatModule, IDisposable
{
    public bool CanHandleFight(CombatController.CombatData combatData) => rotationSolverRebornIpc.IsEnabled;

    public bool Start(CombatController.CombatData combatData) => rotationSolverRebornIpc.RotationAuto();

    public bool Stop()
    {
        rotationSolverRebornIpc.RotationStop();
        return true;
    }

    public void Update(IGameObject gameObject)
    {
    }

    public bool CanAttack(IBattleNpc target) => true;

    public void Dispose() => Stop();

    [PublicAPI]
    private enum StateCommandType : byte
    {
        Off,
        Auto,
        TargetOnly,
        Manual
    }
}
