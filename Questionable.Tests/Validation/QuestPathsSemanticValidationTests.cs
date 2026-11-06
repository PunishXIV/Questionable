// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @alydevs

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Questionable.Domain;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Questionable.Validation;
using Questionable.Validation.Validators;
using Xunit;

namespace Questionable.Tests.Validation;

// Only validators independent of runtime Dalamud services belong in this test gate.
public sealed class QuestPathsSemanticValidationTests
{
    private static IReadOnlyList<IQuestValidator> BuildValidators() =>
    [
        new UniqueStartStopValidator(),
    ];

    [Theory]
    [ClassData(typeof(EmbeddedQuestLoader))]
    public void QuestShouldPassSemanticValidators(EmbeddedQuest embedded)
    {
        ElementId questId = ExtractQuestId(embedded)
            ?? throw new Xunit.Sdk.XunitException(
                $"Could not derive ElementId from '{embedded.ManifestName}'. " +
                "Expected the filename to start with '<id>_' (see QuestRegistry.ExtractQuestIdFromName).");

        QuestRoot root;
        using (var stream = embedded.OpenStream())
        {
            root = JsonSerializer.Deserialize<QuestRoot>(stream)
                   ?? throw new Xunit.Sdk.XunitException(
                       $"'{embedded.ManifestName}' deserialized to a null QuestRoot.");
        }

        Quest quest = QuestTestData.CreateQuest(questId,
            [.. root.QuestSequence]);

        List<ValidationIssue> errors = BuildValidators()
            .SelectMany(v => v.Validate(quest))
            .Where(issue => issue.Severity == EIssueSeverity.Error)
            .ToList();

        if (errors.Count > 0)
        {
            string details = string.Join(
                System.Environment.NewLine,
                errors.Select(FormatIssue));
            Assert.Fail(
                $"Quest '{embedded.ManifestName}' failed semantic validation:" +
                System.Environment.NewLine + details);
        }
    }

    private static ElementId? ExtractQuestId(EmbeddedQuest embedded)
    {
        // Mirrors QuestRegistry.ExtractQuestIdFromName: take the segment after the last '/',
        // then the '<id>_' prefix before the first underscore.
        string name = embedded.ManifestName[(embedded.ManifestName.LastIndexOf('/') + 1)..];
        name = name[..^".json".Length];

        int underscore = name.IndexOf('_', System.StringComparison.Ordinal);
        if (underscore < 0)
            return null;

        return ElementId.TryFromString(name[..underscore], out ElementId? id) ? id : null;
    }

    private static string FormatIssue(ValidationIssue issue) =>
        $"  [{issue.Type}] sequence={issue.Sequence?.ToString(CultureInfo.InvariantCulture) ?? "-"} " +
        $"step={issue.Step?.ToString(CultureInfo.InvariantCulture) ?? "-"}: {issue.Description}";
}
