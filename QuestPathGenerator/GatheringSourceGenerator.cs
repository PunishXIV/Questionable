using Json.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Gathering;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator;

[Generator]
[SuppressMessage("MicrosoftCodeAnalysisReleaseTracking", "RS2008")]
public class GatheringSourceGenerator : ISourceGenerator
{
    private static readonly DiagnosticDescriptor InvalidJson = new("GPG0001",
        "Invalid JSON",
        "Invalid gathering file: {0}",
        nameof(GatheringSourceGenerator),
        DiagnosticSeverity.Error,
        true);

    public void Initialize(GeneratorInitializationContext context)
    {
        // No initialization required for this generator.
    }

    public void Execute(GeneratorExecutionContext context)
    {
        // Find schema definition
        AdditionalText? gatheringSchema =
            context.AdditionalFiles.SingleOrDefault(x => Path.GetFileName(x.Path) == "gatheringlocation-v1.json");
        if (gatheringSchema != null)
            GenerateGatheringSource(context, gatheringSchema);
    }

    private void GenerateGatheringSource(GeneratorExecutionContext context, AdditionalText jsonSchemaFile)
    {
        var gatheringSchema = JsonSchema.FromText(jsonSchemaFile.GetText()!.ToString());
        var jsonSchemaFiles = Utils.RegisterSchemas(context);

        List<(ushort, GatheringRoot)> gatheringLocations = [];
        foreach (var (id, node) in Utils.GetAdditionalFiles(context, jsonSchemaFiles, gatheringSchema, InvalidJson,
                     ushort.Parse))
        {
            var gatheringLocation = node.Deserialize<GatheringRoot>()!;
            gatheringLocations.Add((id, gatheringLocation));
        }

        if (gatheringLocations.Count == 0)
            return;

        var partitionedLocations = gatheringLocations
            .OrderBy(x => x.Item1)
            .GroupBy(x => $"LoadLocation{x.Item1 / 100}")
            .ToList();

        var methods = Utils.CreateMethods("LoadLocations", partitionedLocations, CreateInitializer);

        var code =
            CompilationUnit()
                .WithUsings(
                    List(
                        new[]
                        {
                            UsingDirective(
                                IdentifierName("System")),
                            UsingDirective(
                                QualifiedName(
                                    IdentifierName("System"),
                                    IdentifierName("Numerics"))),
                            UsingDirective(
                                QualifiedName(
                                    IdentifierName("System"),
                                    IdentifierName("IO"))),
                            UsingDirective(
                                QualifiedName(
                                    QualifiedName(
                                        IdentifierName("System"), IdentifierName("Collections")),
                                    IdentifierName("Generic"))),
                            UsingDirective(
                                QualifiedName(
                                    QualifiedName(
                                        IdentifierName("Questionable"),
                                        IdentifierName("Model")),
                                    IdentifierName("Gathering"))),
                            UsingDirective(
                                QualifiedName(
                                    QualifiedName(
                                        IdentifierName("Questionable"),
                                        IdentifierName("Model")),
                                    IdentifierName("Questing"))),
                            UsingDirective(
                                QualifiedName(
                                    QualifiedName(
                                        IdentifierName("Questionable"),
                                        IdentifierName("Model")),
                                    IdentifierName("Common")))
                        }))
                .WithMembers(
                    SingletonList<MemberDeclarationSyntax>(
                        FileScopedNamespaceDeclaration(
                                QualifiedName(
                                    IdentifierName("Questionable"),
                                    IdentifierName("GatheringPaths")))
                            .WithMembers(
                                SingletonList<MemberDeclarationSyntax>(
                                    ClassDeclaration("AssemblyGatheringLocationLoader")
                                        .WithModifiers(
                                            TokenList(Token(SyntaxKind.PartialKeyword)))
                                        .WithMembers(List<MemberDeclarationSyntax>(methods))))))
                .NormalizeWhitespace();

        // Add the source code to the compilation.
        context.AddSource("AssemblyGatheringLocationLoader.g.cs", code.ToFullString());
    }

    private static StatementSyntax[] CreateInitializer(List<(ushort QuestId, GatheringRoot Root)> quests)
    {
        List<StatementSyntax> statements = [];

        foreach (var quest in quests)
        {
            statements.Add(
                ExpressionStatement(
                    InvocationExpression(
                            IdentifierName("AddLocation"))
                        .WithArgumentList(
                            ArgumentList(
                                SeparatedList<ArgumentSyntax>(
                                    new SyntaxNodeOrToken[]
                                    {
                                        Argument(
                                            LiteralExpression(SyntaxKind.NumericLiteralExpression,
                                                Literal(quest.QuestId))),
                                        Token(SyntaxKind.CommaToken),
                                        Argument(CreateGatheringRootExpression(quest.QuestId, quest.Root))
                                    })))));
        }

        return statements.ToArray();
    }

    private static ObjectCreationExpressionSyntax CreateGatheringRootExpression(ushort locationId, GatheringRoot root)
    {
        try
        {
            var emptyRoot = new GatheringRoot();
            return ObjectCreationExpression(
                    IdentifierName(nameof(GatheringRoot)))
                .WithInitializer(
                    InitializerExpression(
                        SyntaxKind.ObjectInitializerExpression,
                        SeparatedList<ExpressionSyntax>(
                            SyntaxNodeList(
                                AssignmentList(nameof(GatheringRoot.Author), root.Author).AsSyntaxNodeOrToken(),
                                AssignmentList(nameof(GatheringRoot.Steps), root.Steps)
                                    .AsSyntaxNodeOrToken(),
                                Assignment(nameof(GatheringRoot.FlyBetweenNodes), root.FlyBetweenNodes,
                                        emptyRoot.FlyBetweenNodes)
                                    .AsSyntaxNodeOrToken(),
                                AssignmentList(nameof(GatheringRoot.ExtraQuestItems), root.ExtraQuestItems).AsSyntaxNodeOrToken(),
                                AssignmentList(nameof(GatheringRoot.Groups), root.Groups).AsSyntaxNodeOrToken()))));
        }
        catch (Exception e)
        {
            throw new Exception($"GatheringGen[{locationId}]: {e.Message}", e);
        }
    }
}
