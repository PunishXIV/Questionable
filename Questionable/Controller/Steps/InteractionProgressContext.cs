using FFXIVClientStructs.FFXIV.Client.Game;
using System;

namespace Questionable.Controller.Steps;

internal sealed class InteractionProgressContext
{
    private bool _firstUpdateDone;
    public bool CheckSequence { get; private set; }
    public int CurrentSequence { get; private set; }

    private InteractionProgressContext(bool checkSequence, int currentSequence)
    {
        CheckSequence = checkSequence;
        CurrentSequence = currentSequence;
    }

    public static unsafe InteractionProgressContext Create(bool checkSequence)
    {
        if (!checkSequence)
        {
            // this is a silly hack; we assume that the previous cast was successful
            // if not for this, we'd instantly be seen as interrupted
            ActionManager.Instance()->CastTimeElapsed = ActionManager.Instance()->CastTimeTotal;
        }

        return new InteractionProgressContext(checkSequence, ActionManager.Instance()->LastUsedActionSequence);
    }

    private static unsafe (bool, InteractionProgressContext?) FromActionUseInternal(Func<bool> func)
    {
        int oldSequence = ActionManager.Instance()->LastUsedActionSequence;
        if (!func())
            return (false, null);
        int newSequence = ActionManager.Instance()->LastUsedActionSequence;
        if (oldSequence == newSequence)
            return (true, null);
        return (true, Create(true));
    }

    public static InteractionProgressContext? FromActionUse(Func<bool> func)
    {
        return FromActionUseInternal(func).Item2;
    }

    public static InteractionProgressContext? FromActionUseOrDefault(Func<bool> func)
    {
        var result = FromActionUseInternal(func);
        if (!result.Item1)
            return null;
        return result.Item2 ?? Create(false);
    }

    public unsafe void Update()
    {
        if (!_firstUpdateDone)
        {
            int lastSequence = ActionManager.Instance()->LastUsedActionSequence;
            if (!CheckSequence && lastSequence > CurrentSequence)
            {
                CheckSequence = true;
                CurrentSequence = lastSequence;
            }

            _firstUpdateDone = true;
        }
    }

    public unsafe bool WasSuccessful()
    {
        if (CheckSequence)
        {
            if (CurrentSequence != ActionManager.Instance()->LastUsedActionSequence ||
                CurrentSequence != ActionManager.Instance()->LastHandledActionSequence)
                return false;
        }

        return ActionManager.Instance()->CastTimeElapsed > 0 &&
               Math.Abs(ActionManager.Instance()->CastTimeElapsed - ActionManager.Instance()->CastTimeTotal) < 0.001f;
    }

    public unsafe bool WasInterrupted()
    {
        if (CheckSequence)
        {
            if (CurrentSequence == ActionManager.Instance()->LastHandledActionSequence &&
                CurrentSequence == ActionManager.Instance()->LastUsedActionSequence)
                return false;
        }

        return ActionManager.Instance()->CastTimeElapsed == 0 &&
               ActionManager.Instance()->CastTimeTotal > 0;
    }

    public override string ToString() =>
        $"IPCtx({(CheckSequence ? CurrentSequence : "-")} - {WasSuccessful()}, {WasInterrupted()})";
}
