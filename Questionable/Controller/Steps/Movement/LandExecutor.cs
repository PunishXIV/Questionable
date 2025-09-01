using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Microsoft.Extensions.Logging;
using System;

namespace Questionable.Controller.Steps.Movement;

internal sealed class LandExecutor(IClientState clientState, ICondition condition, ILogger<LandExecutor> logger)
    : TaskExecutor<LandTask>
{
    private bool _landing;
    private DateTime _continueAt;

    protected override bool Start()
    {
        if (!condition[ConditionFlag.InFlight])
        {
            logger.LogInformation("Not flying, not attempting to land");
            return false;
        }

        _landing = AttemptLanding();
        _continueAt = DateTime.Now.AddSeconds(0.25);
        return true;
    }

    public override ETaskResult Update()
    {
        if (DateTime.Now < _continueAt)
            return ETaskResult.StillRunning;

        if (condition[ConditionFlag.InFlight])
        {
            if (!_landing)
            {
                _landing = AttemptLanding();
                _continueAt = DateTime.Now.AddSeconds(0.25);
            }

            return ETaskResult.StillRunning;
        }

        return ETaskResult.TaskComplete;
    }

    private unsafe bool AttemptLanding()
    {
        var character = (Character*)(clientState.LocalPlayer?.Address ?? 0);
        if (character != null)
        {
            if (ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 23) == 0)
            {
                logger.LogInformation("Attempting to land");
                return ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23);
            }
        }

        return false;
    }

    public override bool ShouldInterruptOnDamage() => false;
}
