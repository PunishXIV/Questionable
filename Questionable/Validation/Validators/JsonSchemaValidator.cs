using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using Json.Schema;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.QuestPaths;
namespace Questionable.Validation.Validators;

internal sealed class JsonSchemaValidator : IQuestValidator
{
    private readonly Dictionary<ElementId, JsonNode> _questNodes = [];
    private JsonSchema? _questSchema;

    public JsonSchemaValidator()
    {
        SchemaRegistry.Global.Register(
            new("https://qstxiv.github.io/schema/common-aethernetshard.json"),
            JsonSchema.FromStream(AssemblyModelLoader.CommonAethernetShardStream).AsTask().Result);
        SchemaRegistry.Global.Register(
            new("https://qstxiv.github.io/schema/common-aetheryte.json"),
            JsonSchema.FromStream(AssemblyModelLoader.CommonAetheryteStream).AsTask().Result);
        SchemaRegistry.Global.Register(
            new("https://qstxiv.github.io/schema/common-classjob.json"),
            JsonSchema.FromStream(AssemblyModelLoader.CommonClassJobStream).AsTask().Result);
        SchemaRegistry.Global.Register(
            new("https://qstxiv.github.io/schema/common-completionflags.json"),
            JsonSchema.FromStream(AssemblyModelLoader.CommonCompletionFlagsStream).AsTask().Result);
        SchemaRegistry.Global.Register(
            new("https://qstxiv.github.io/schema/common-vector3.json"),
            JsonSchema.FromStream(AssemblyModelLoader.CommonVector3Stream).AsTask().Result);
    }

    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        _questSchema ??= JsonSchema.FromStream(AssemblyQuestLoader.QuestSchemaStream).AsTask().Result;

        if (_questNodes.TryGetValue(quest.Id, out JsonNode? questNode))
        {
            EvaluationResults evaluationResult = _questSchema.Evaluate(questNode, new()
            {
                Culture = CultureInfo.InvariantCulture,
                OutputFormat = OutputFormat.List
            });
            if (!evaluationResult.IsValid)
            {
                yield return new()
                {
                    ElementId = quest.Id,
                    Sequence = null,
                    Step = null,
                    Type = EIssueType.InvalidJsonSchema,
                    Severity = EIssueSeverity.Error,
                    Description = "JSON Validation failed"
                };
            }
        }
    }

    public void Reset() => _questNodes.Clear();

    public void Enqueue(ElementId elementId, JsonNode questNode) => _questNodes[elementId] = questNode;
}
