using Microsoft.Extensions.Logging;
using Questionable.Model;
using Questionable.Model.Questing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Questionable.Validation;

internal sealed class QuestValidator
{
    private readonly IReadOnlyList<IQuestValidator> _validators;
    private readonly ILogger<QuestValidator> _logger;

    private List<ValidationIssue> _validationIssues = new();

    public QuestValidator(IEnumerable<IQuestValidator> validators, ILogger<QuestValidator> logger)
    {
        _validators = validators.ToList();
        _logger = logger;

        _logger.LogInformation("Validators: {Validators}",
            string.Join(", ", _validators.Select(x => x.GetType().Name)));
    }

    public IReadOnlyList<ValidationIssue> Issues => _validationIssues;
    public int IssueCount => _validationIssues.Count;
    public int ErrorCount => _validationIssues.Count(x => x.Severity == EIssueSeverity.Error);

    public void Reset()
    {
        foreach (var validator in _validators)
            validator.Reset();
        _validationIssues.Clear();
    }

    public void Validate(IEnumerable<Quest> quests)
    {
        Task.Factory.StartNew(() =>
        {
            try
            {
                _validationIssues.Clear();

                List<ValidationIssue> issues = [];
                Dictionary<EAlliedSociety, int> disabledTribeQuests = new();
                foreach (var quest in quests)
                {
                    foreach (var validator in _validators)
                    {
                        foreach (var issue in validator.Validate(quest))
                        {
                            /*
                            var level = issue.Severity == EIssueSeverity.Error
                                ? LogLevel.Warning
                                : LogLevel.Debug;
                            _logger.Log(level,
                                "Validation failed: {QuestId} ({QuestName}) / {QuestSequence} / {QuestStep} - {Description}",
                                issue.ElementId, quest.Info.Name, issue.Sequence, issue.Step, issue.Description);
                            */
                            if (issue.Type == EIssueType.QuestDisabled && quest.Info.AlliedSociety != EAlliedSociety.None)
                            {
                                disabledTribeQuests.TryAdd(quest.Info.AlliedSociety, 0);
                                disabledTribeQuests[quest.Info.AlliedSociety]++;
                            }
                            else
                                issues.Add(issue);
                        }
                    }
                }

                var disabledQuests = issues
                    .Where(x => x.Type == EIssueType.QuestDisabled)
                    .Select(x => x.ElementId)
                    .ToList();

                _validationIssues = issues
                    .Where(x => !disabledQuests.Contains(x.ElementId) || x.Type == EIssueType.QuestDisabled)
                    .OrderBy(x => x.ElementId)
                    .ThenBy(x => x.Sequence)
                    .ThenBy(x => x.Step)
                    .ThenBy(x => x.Description)
                    .Concat(DisabledTribesAsIssues(disabledTribeQuests))
                    .ToList();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unable to validate quests");
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public List<ValidationIssue> GetIssues(ElementId elementId) =>
        _validationIssues.Where(x => x.ElementId == elementId).ToList();

    private static IEnumerable<ValidationIssue> DisabledTribesAsIssues(Dictionary<EAlliedSociety, int> disabledTribeQuests)
    {
        return disabledTribeQuests
            .OrderBy(x => x.Key)
            .Select(x => new ValidationIssue
            {
                ElementId = null,
                Sequence = null,
                Step = null,
                AlliedSociety = x.Key,
                Type = EIssueType.QuestDisabled,
                Severity = EIssueSeverity.None,
                Description = $"{x.Value} disabled quest(s)",
            });
    }
}
