using System.Globalization;
using System.Text.Json.Nodes;
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
            Assert.Fail($"Quest '{quest.ManifestName}' validation failed");
    }
}
