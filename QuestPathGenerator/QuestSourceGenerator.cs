using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using Json.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator;

/// <summary>
/// A sample source generator that creates C# classes based on the text file (in this case, Domain Driven Design ubiquitous language registry).
/// When using a simple text file as a baseline, we can create a non-incremental source generator.
/// </summary>
[Generator]
[SuppressMessage("MicrosoftCodeAnalysisReleaseTracking", "RS2008")]
public class QuestSourceGenerator : ISourceGenerator
{
    private static readonly DiagnosticDescriptor InvalidJson = new("QSG0001",
        "Invalid JSON",
        "Invalid quest file {0}",
        nameof(QuestSourceGenerator),
        DiagnosticSeverity.Error,
        true);

    public void Initialize(GeneratorInitializationContext context)
    {
        // No initialization required for this generator.
    }

    public void Execute(GeneratorExecutionContext context)
    {
        // Find schema definition
        AdditionalText? questSchema =
            context.AdditionalFiles.SingleOrDefault(x => Path.GetFileName(x.Path) == "quest-v1.json");
        if (questSchema != null)
            GenerateQuestSource(context, questSchema);
    }

    private void GenerateQuestSource(GeneratorExecutionContext context, AdditionalText jsonSchemaFile)
    {
        var questSchema = JsonSchema.FromText(jsonSchemaFile.GetText()!.ToString());
        var jsonSchemaFiles = Utils.RegisterSchemas(context);

        List<(ElementId, QuestRoot)> quests = [];
        foreach (var (id, node) in Utils.GetAdditionalFiles(context, jsonSchemaFiles, questSchema, InvalidJson,
                     ElementId.FromString))
        {
            var quest = node.Deserialize<QuestRoot>()!;
            if (quest.Disabled)
            {
                quest.Author = [];
                quest.QuestSequence = [];
            }

            quests.Add((id, quest));
        }

        if (quests.Count == 0)
            return;

        var partitionedQuests = quests
            .OrderBy(x => x.Item1.Value)
            .GroupBy(x => $"LoadQuests{x.Item1.Value / 50}")
            .ToList();

        var methods = Utils.CreateMethods("LoadQuests", partitionedQuests, CreateInitializer);

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
                                    IdentifierName("QuestPaths")))
                            .WithMembers(
                                SingletonList<MemberDeclarationSyntax>(
                                    ClassDeclaration("AssemblyQuestLoader")
                                        .WithModifiers(
                                            TokenList(Token(SyntaxKind.PartialKeyword)))
                                        .WithMembers(List<MemberDeclarationSyntax>(methods))))))
                .NormalizeWhitespace();

        // Add the source code to the compilation.
        context.AddSource("AssemblyQuestLoader.g.cs", code.ToFullString());
    }

    private static StatementSyntax[] CreateInitializer(List<(ElementId QuestId, QuestRoot Root)> quests)
    {
        List<StatementSyntax> statements = [];

        foreach (var quest in quests)
        {
            statements.Add(
                ExpressionStatement(
                    InvocationExpression(
                            IdentifierName("AddQuest"))
                        .WithArgumentList(
                            ArgumentList(
                                SeparatedList<ArgumentSyntax>(
                                    new SyntaxNodeOrToken[]
                                    {
                                        Argument(LiteralValue(quest.QuestId)),
                                        Token(SyntaxKind.CommaToken),
                                        Argument(CreateQuestRootExpression(quest.QuestId, quest.Root))
                                    })))));
        }

        return statements.ToArray();
    }

    private static ObjectCreationExpressionSyntax CreateQuestRootExpression(ElementId questId, QuestRoot quest)
    {
        try
        {
            QuestRoot emptyQuest = new();
            return ObjectCreationExpression(
                    IdentifierName(nameof(QuestRoot)))
                .WithInitializer(
                    InitializerExpression(
                        SyntaxKind.ObjectInitializerExpression,
                        SeparatedList<ExpressionSyntax>(
                            SyntaxNodeList(
                                AssignmentList(nameof(QuestRoot.Author), quest.Author)
                                    .AsSyntaxNodeOrToken(),
                                Assignment(nameof(QuestRoot.Disabled), quest.Disabled, emptyQuest.Disabled).AsSyntaxNodeOrToken(),
                                Assignment(nameof(QuestRoot.Interruptible), quest.Interruptible, emptyQuest.Interruptible).AsSyntaxNodeOrToken(),
                                Assignment(nameof(QuestRoot.Comment), quest.Comment, emptyQuest.Comment)
                                    .AsSyntaxNodeOrToken(),
                                AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    IdentifierName(nameof(QuestRoot.QuestSequence)),
                                    CreateQuestSequence(quest.QuestSequence))))));
        }
        catch (Exception e)
        {
            throw new Exception($"QuestGen[{questId}]: {e.Message}", e);
        }
    }

    private static ExpressionSyntax CreateQuestSequence(List<QuestSequence> sequences)
    {
        return CollectionExpression(
            SeparatedList<CollectionElementSyntax>(
                sequences.SelectMany(sequence => new SyntaxNodeOrToken[]
                {
                    ExpressionElement(
                        ObjectCreationExpression(
                                IdentifierName(nameof(QuestSequence)))
                            .WithInitializer(
                                InitializerExpression(
                                    SyntaxKind.ObjectInitializerExpression,
                                    SeparatedList<ExpressionSyntax>(
                                        SyntaxNodeList(
                                            Assignment<int?>(nameof(QuestSequence.Sequence), sequence.Sequence, null)
                                                .AsSyntaxNodeOrToken(),
                                            Assignment(nameof(QuestSequence.Comment), sequence.Comment, null)
                                                .AsSyntaxNodeOrToken(),
                                            AssignmentList(nameof(QuestSequence.Steps), sequence.Steps)
                                                .AsSyntaxNodeOrToken()))))),
                    Token(SyntaxKind.CommaToken),
                }.ToArray())));
    }
}
