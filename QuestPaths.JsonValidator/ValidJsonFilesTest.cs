using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;
using Questionable.Model;
using Questionable.QuestPaths;
using Xunit;

namespace QuestPaths.JsonValidator;

public sealed class ValidJsonFilesTest
{
    private static readonly JsonSchema QuestSchema = JsonSchema.FromStream(AssemblyQuestLoader.QuestSchemaStream).AsTask().Result;

    static ValidJsonFilesTest()
    {
        SchemaRegistry.Global.Register(
            new Uri("https://qstxiv.github.io/schema/common-aethernetshard.json"),
            JsonSchema.FromStream(AssemblyModelLoader.CommonAethernetShardStream).AsTask().Result);
        SchemaRegistry.Global.Register(
            new Uri("https://qstxiv.github.io/schema/common-aetheryte.json"),
            JsonSchema.FromStream(AssemblyModelLoader.CommonAetheryteStream).AsTask().Result);
        SchemaRegistry.Global.Register(
            new Uri("https://qstxiv.github.io/schema/common-classjob.json"),
            JsonSchema.FromStream(AssemblyModelLoader.CommonClassJobStream).AsTask().Result);
        SchemaRegistry.Global.Register(
            new Uri("https://qstxiv.github.io/schema/common-completionflags.json"),
            JsonSchema.FromStream(AssemblyModelLoader.CommonCompletionFlagsStream).AsTask().Result);
        SchemaRegistry.Global.Register(
            new Uri("https://qstxiv.github.io/schema/common-vector3.json"),
            JsonSchema.FromStream(AssemblyModelLoader.CommonVector3Stream).AsTask().Result);
    }

    [Theory]
    [ClassData(typeof(TestQuestLoader))]
    public void QuestShouldValidateAsJson(QuestWrapper quest)
    {
        JsonNode questNode = JsonNode.Parse(quest.AsStream()) ?? throw new InvalidDataException("no quest stream");

        EvaluationResults evaluationResult = QuestSchema.Evaluate(questNode, new EvaluationOptions
        {
            Culture = CultureInfo.InvariantCulture,
            OutputFormat = OutputFormat.List
        });

        if (!evaluationResult.IsValid)
        {
            IEnumerable<string> failures = evaluationResult.Details
                .Where(detail => detail is { IsValid: false, HasErrors: true })
                // A failing 'if' condition inside an allOf branch is expected (the branch simply doesn't apply),
                // so only report assertion failures that aren't part of an 'if' probe.
                .Where(detail => !detail.EvaluationPath.ToString().Contains("/if/"))
                .SelectMany(detail => detail.Errors!.Select(error =>
                    $"  instance '{detail.InstanceLocation}' (schema '{detail.EvaluationPath}'): {error.Key} - {Unescape(error.Value)}"));

            Assert.Fail($"Quest '{quest.ManifestName}' validation failed:{Environment.NewLine}"
                        + string.Join(Environment.NewLine, failures));
        }
    }

    /// <summary>
    /// Decodes <c>\uXXXX</c> escapes that System.Text.Json's default HTML-safe encoder produces
    /// (e.g. <c>"</c> as <c>\u0022</c>, <c>'</c> as <c>\u0027</c>) so error messages render readably.
    /// </summary>
    private static string Unescape(string value) =>
        Regex.Replace(value, @"\\u([0-9a-fA-F]{4})",
            match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());
}
