namespace Questionable.Windows.JournalComponents;

// Dalamud has no quest-completed event, so the open journal re-counts on a timer.
internal sealed class JournalRefresh(TimeSpan interval)
{
    private DateTime _next = DateTime.MinValue;

    // True when the recount found a change, so the filters need applying again.
    public bool Tick(DateTime now, Func<bool> recount)
    {
        if (now < _next)
            return false;

        _next = now + interval;
        return recount();
    }
}
