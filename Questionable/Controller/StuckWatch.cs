using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Movement;
using Questionable.Controller.Steps.Shared;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class StuckWatch(TimeProvider? timeProvider = null)
{
    internal const float MoveThreshold = 0.5f;
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private (ElementId QuestId, byte Sequence, int Step)? _step;
    private ITask? _task;
    private Vector3 _position;
    private long _since;

    public int Tries { get; private set; }

    // A new task instance counts as progress; the retry count only resets on a new step.
    public EResult Update((ElementId QuestId, byte Sequence, int Step) step, ITask? task, Vector3 position,
        bool paused, TimeSpan delay, int maxTries)
    {
        if (_step != step)
        {
            _step = step;
            Tries = 0;
            Restart(task, position);
            return EResult.None;
        }

        if (paused || !ReferenceEquals(task, _task) || Vector3.Distance(position, _position) > MoveThreshold)
        {
            Restart(task, position);
            return EResult.None;
        }

        if (_clock.GetElapsedTime(_since) < delay)
            return EResult.None;

        Restart(task, position);
        return ++Tries > maxTries ? EResult.Stop : EResult.Retry;
    }

    public void Reset()
    {
        _step = null;
        _task = null;
        Tries = 0;
    }

    // Movement can be redone on any step; an interaction only where doing it twice is harmless
    // (not item use, purchases, abandoning quests, combat or duties).
    internal static bool CanRetry(EInteractionType? stepType, ITask? task) =>
        IsMovement(task) || (stepType is { } type && SafeSteps.Contains(type) && IsInteraction(task));

    private static readonly HashSet<EInteractionType> SafeSteps =
    [
        EInteractionType.Interact, EInteractionType.WalkTo, EInteractionType.AcceptQuest,
        EInteractionType.CompleteQuest, EInteractionType.UnlockTaxiStand, EInteractionType.AttuneAetheryte,
        EInteractionType.AttuneAethernetShard, EInteractionType.AttuneAetherCurrent,
        EInteractionType.RegisterFreeOrFavoredAetheryte, EInteractionType.Say, EInteractionType.Emote,
    ];

    private static bool IsMovement(ITask? task) =>
        task is MoveTask or MountStep.MountTask or LandTask or Jump.SingleTask or Jump.RepeatedTask
            or AetheryteShortcut.MoveAway;

    private static bool IsInteraction(ITask? task) =>
        task is Interact.Task or Aetheryte.Attune or AethernetShard.Attune or AetherCurrent.Attune
            or AetheryteFreeOrFavored.Register or Say.Task or Emote.UseOnObject or Emote.UseOnSelf
            or WaitCondition.Task or WaitAtEnd.TerritoryEntered or WaitAtEnd.CompletionFlags
            or WaitAtEnd.QuestAccepted or WaitAtEnd.QuestCompleted;

    private void Restart(ITask? task, Vector3 position)
    {
        _task = task;
        _position = position;
        _since = _clock.GetTimestamp();
    }

    internal enum EResult
    {
        None,
        Retry,
        Stop,
    }
}
