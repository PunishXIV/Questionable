using Json.Schema;
using Questionable.Model;
using Questionable.QuestPaths;
using System.Globalization;
using System.Text.Json.Nodes;
using Xunit;

namespace QuestPaths.JsonValidator;

public sealed class ValidJsonFilesTest
{
    private static readonly JsonSchema QuestSchema;

    static ValidJsonFilesTest()
    {
        SchemaRegistry.Global.Register(
            new Uri("https://github.com/PunishXIV/Questionable/raw/refs/heads/main/Questionable.Model/common-aethernetshard.json"),
            JsonSchema.FromStream(AssemblyModelLoader.CommonAethernetShard).AsTask().Result);
        SchemaRegistry.Global.Register(
            new Uri("https://github.com/PunishXIV/Questionable/raw/refs/heads/main/Questionable.Model/common-aetheryte.json"),
            JsonSchema.FromStream(AssemblyModelLoader.CommonAetheryte).AsTask().Result);
        SchemaRegistry.Global.Register(
            new Uri("https://github.com/PunishXIV/Questionable/raw/refs/heads/main/Questionable.Model/common-classjob.json"),
            JsonSchema.FromStream(AssemblyModelLoader.CommonClassJob).AsTask().Result);
        SchemaRegistry.Global.Register(
            new Uri("https://github.com/PunishXIV/Questionable/raw/refs/heads/main/Questionable.Model/common-completionflags.json"),
            JsonSchema.FromStream(AssemblyModelLoader.CommonCompletionFlags).AsTask().Result);
        SchemaRegistry.Global.Register(
            new Uri("https://github.com/PunishXIV/Questionable/raw/refs/heads/main/Questionable.Model/common-vector3.json"),
            JsonSchema.FromStream(AssemblyModelLoader.CommonVector3).AsTask().Result);

        QuestSchema = JsonSchema.FromStream(AssemblyQuestLoader.QuestSchema).AsTask().Result;
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
