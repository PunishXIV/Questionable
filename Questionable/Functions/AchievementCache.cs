namespace Questionable.Functions;

// The game only loads achievement data once the Achievements window has been opened. Open it at most once per
// login and remember completions, since they never revert.
internal sealed class AchievementCache(
    Func<string?> contentId,
    Func<bool> isLoaded,
    Func<int, bool> isComplete,
    Action show,
    Action hide)
{
    private readonly HashSet<int> _completed = [];
    private string? _contentId;
    private bool _requested;
    private bool _loaded;

    public bool IsComplete(params int[] achievementIds)
    {
        string? current = contentId();
        if (current != _contentId)
        {
            _contentId = current;
            _completed.Clear();
            _loaded = false;
        }

        if (achievementIds.All(_completed.Contains))
            return true;

        if (!isLoaded())
        {
            if (!_requested && !_loaded)
            {
                show();
                _requested = true;
            }

            return false;
        }

        if (_requested)
        {
            hide();
            _requested = false;
        }

        _loaded = true;
        foreach (int id in achievementIds)
        {
            if (isComplete(id))
                _completed.Add(id);
        }

        return achievementIds.All(_completed.Contains);
    }
}
