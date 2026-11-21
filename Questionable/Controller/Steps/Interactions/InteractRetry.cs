namespace Questionable.Controller.Steps.Interactions;

// Mounted, in the air or before the object has loaded, the game takes an interaction without doing it.
internal sealed class InteractRetry(DateTime now)
{
    internal const int MaxTries = 10;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    private DateTime _next = now + Interval;
    private int _tries;

    public bool Due(DateTime now, bool occupied)
    {
        if (occupied)
        {
            _next = now + Interval;
            return false;
        }

        if (now < _next)
            return false;

        if (++_tries > MaxTries)
            throw new TaskException("The interaction didn't go through after several tries");

        _next = now + Interval;
        return true;
    }
}
