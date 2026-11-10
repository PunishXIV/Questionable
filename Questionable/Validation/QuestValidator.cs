using static Questionable.Utils.CacheUtils;
namespace Questionable.Validation;

[RegisterSingleton]
internal sealed class QuestValidator
{
    private readonly ILogger<QuestValidator> _logger;
    private readonly IReadOnlyList<IQuestValidator> _validators;

    private List<ValidationIssue> _issues = [];

    public QuestValidator(IEnumerable<IQuestValidator> validators, ILogger<QuestValidator> logger)
    {
        _validators = validators.ToList();
        _logger = logger;

        _logger.LogInformation("Validators: {Validators}",
            string.Join(", ", _validators.Select(x => x.GetType().Name)));
    }

    public IReadOnlyList<ValidationIssue> Issues => _issues;
    private CachedValue<int> _errorCount = new(ttlSeconds: 1);
    public volatile bool Running;
    public int IssueCount => _issues.Count;
    public int ErrorCount => _errorCount.Get(() => _issues.Count(x => x.Severity == EIssueSeverity.Error));

    public void Reset()
    {
        foreach (IQuestValidator validator in _validators)
            validator.Reset();
        _issues.Clear();
    }

    public void Validate(IEnumerable<Quest> quests)
    {
        Task.Factory.StartNew(() =>
        {
            try
            {
                if (Running)
                    return;
                Running = true;
                _issues.Clear();

                List<ValidationIssue> issues = [];
                foreach (Quest quest in quests)
                {
                    foreach (IQuestValidator validator in _validators)
                    {
                        try
                        {
                            foreach (ValidationIssue issue in validator.Validate(quest))
                            {
                                issues.Add(issue);
                            }
                        }
                        catch (ArgumentException e)
                        {
                            _logger.LogError(e, $"Unable to validate {quest.Info.QuestId} {quest.Info.Name}");
                        }
                    }
                }

                List<ElementId?> disabledQuests = issues
                    .Where(x => x.Type == EIssueType.QuestDisabled)
                    .Select(x => x.ElementId)
                    .ToList();

                _issues = issues
                    .Where(x => !disabledQuests.Contains(x.ElementId) || x.Type == EIssueType.QuestDisabled)
                    .OrderBy(x => x.ElementId)
                    .ThenBy(x => x.Sequence)
                    .ThenBy(x => x.Step)
                    .ThenBy(x => x.Description)
                    .ToList();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unable to validate quests");
            }
            Running = false;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public List<ValidationIssue> GetIssues(ElementId elementId) => _issues.Where(x => x.ElementId == elementId).ToList();
}
