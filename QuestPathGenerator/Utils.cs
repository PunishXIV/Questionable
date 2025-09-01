using Json.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Questionable.QuestPathGenerator;

public static class Utils
{
    public static List<AdditionalText> RegisterSchemas(GeneratorExecutionContext context)
    {
        var commonAethernetShardFile =
            context.AdditionalFiles.Single(x => Path.GetFileName(x.Path) == "common-aethernetshard.json");
        var commonAetheryteFile =
            context.AdditionalFiles.Single(x => Path.GetFileName(x.Path) == "common-aetheryte.json");
        var commonClassJobFile =
            context.AdditionalFiles.Single(x => Path.GetFileName(x.Path) == "common-classjob.json");
        var commonCompletionFlagsFile =
            context.AdditionalFiles.Single(x => Path.GetFileName(x.Path) == "common-completionflags.json");
        var commonVector3File = context.AdditionalFiles.Single(x => Path.GetFileName(x.Path) == "common-vector3.json");
        var gatheringSchemaFile =
            context.AdditionalFiles.SingleOrDefault(x => Path.GetFileName(x.Path) == "gatheringlocation-v1.json");
        var questSchemaFile = context.AdditionalFiles.SingleOrDefault(x => Path.GetFileName(x.Path) == "quest-v1.json");

        SchemaRegistry.Global.Register(
            new Uri(
                "https://github.com/PunishXIV/Questionable/raw/refs/heads/main/Questionable.Model/common-aethernetshard.json"),
            JsonSchema.FromText(commonAethernetShardFile.GetText()!.ToString()));
        SchemaRegistry.Global.Register(
            new Uri(
                "https://github.com/PunishXIV/Questionable/raw/refs/heads/main/Questionable.Model/common-aetheryte.json"),
            JsonSchema.FromText(commonAetheryteFile.GetText()!.ToString()));
        SchemaRegistry.Global.Register(
            new Uri(
                "https://github.com/PunishXIV/Questionable/raw/refs/heads/main/Questionable.Model/common-classjob.json"),
            JsonSchema.FromText(commonClassJobFile.GetText()!.ToString()));
        SchemaRegistry.Global.Register(
            new Uri(
                "https://github.com/PunishXIV/Questionable/raw/refs/heads/main/Questionable.Model/common-completionflags.json"),
            JsonSchema.FromText(commonCompletionFlagsFile.GetText()!.ToString()));
        SchemaRegistry.Global.Register(
            new Uri("https://github.com/PunishXIV/Questionable/raw/refs/heads/main/Questionable.Model/common-vector3.json"),
            JsonSchema.FromText(commonVector3File.GetText()!.ToString()));

        if (gatheringSchemaFile != null)
        {
            SchemaRegistry.Global.Register(
                new Uri(
                    "https://github.com/PunishXIV/Questionable/raw/refs/heads/main/GatheringPaths/gatheringlocation-v1.json"),
                JsonSchema.FromText(gatheringSchemaFile.GetText()!.ToString()));
        }

        if (questSchemaFile != null)
        {
            SchemaRegistry.Global.Register(
                new Uri("https://github.com/PunishXIV/Questionable/raw/refs/heads/main/QuestPaths/quest-v1.json"),
                JsonSchema.FromText(questSchemaFile.GetText()!.ToString()));
        }

        List<AdditionalText?> jsonSchemaFiles =
        [
            commonAethernetShardFile,
            commonAetheryteFile,
            commonClassJobFile,
            commonCompletionFlagsFile,
            commonVector3File,
            gatheringSchemaFile,
            questSchemaFile
        ];
        return jsonSchemaFiles.Where(x => x != null).Cast<AdditionalText>().ToList();
    }

    public static IEnumerable<(T, JsonNode)> GetAdditionalFiles<T>(GeneratorExecutionContext context,
        List<AdditionalText> jsonSchemaFiles, JsonSchema jsonSchema, DiagnosticDescriptor invalidJson,
        Func<string, T> idParser)
    {
        foreach (var additionalFile in context.AdditionalFiles)
        {
            if (additionalFile == null || jsonSchemaFiles.Contains(additionalFile))
                continue;

            if (Path.GetExtension(additionalFile.Path) != ".json")
                continue;

            string name = Path.GetFileName(additionalFile.Path);
            if (!name.Contains("_"))
                continue;

            T id = idParser(name.Substring(0, name.IndexOf('_')));

            var text = additionalFile.GetText();
            if (text == null)
                continue;

            var node = JsonNode.Parse(text.ToString());
            if (node == null)
                continue;

            string? schemaLocation = node["$schema"]?.GetValue<string?>();
            if (schemaLocation == null || new Uri(schemaLocation) != jsonSchema.GetId())
                continue;

            var evaluationResult = jsonSchema.Evaluate(node, new EvaluationOptions
            {
                Culture = CultureInfo.InvariantCulture,
                OutputFormat = OutputFormat.List,
            });
            if (evaluationResult.HasErrors)
            {
                var error = Diagnostic.Create(invalidJson,
                    null,
                    Path.GetFileName(additionalFile.Path));
                context.ReportDiagnostic(error);
                continue;
            }

            yield return (id, node);
        }
    }

    public static List<MethodDeclarationSyntax> CreateMethods<TId, TQuest>(string prefix,
        List<IGrouping<string, (TId, TQuest)>> partitions,
        Func<List<(TId, TQuest)>, StatementSyntax[]> toInitializers)
    {
        List<MethodDeclarationSyntax> methods =
        [
            MethodDeclaration(
                    PredefinedType(
                        Token(SyntaxKind.VoidKeyword)),
                    Identifier(prefix))
                .WithModifiers(
                    TokenList(
                        Token(SyntaxKind.PrivateKeyword),
                        Token(SyntaxKind.StaticKeyword)))
                .WithBody(
                    Block(
                        partitions
                            .Select(x =>
                                ExpressionStatement(
                                    InvocationExpression(
                                        IdentifierName(x.Key))))))
        ];

        foreach (var partition in partitions)
        {
            methods.Add(MethodDeclaration(
                    PredefinedType(
                        Token(SyntaxKind.VoidKeyword)),
                    Identifier(partition.Key))
                .WithModifiers(
                    TokenList(
                        Token(SyntaxKind.PrivateKeyword),
                        Token(SyntaxKind.StaticKeyword)))
                .WithBody(
                    Block(toInitializers(partition.ToList()))));
        }

        return methods;
    }
}
