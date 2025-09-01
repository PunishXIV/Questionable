using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Common;
using Questionable.Model.Gathering;
using Questionable.Model.Questing;
using Questionable.QuestPathGenerator.RoslynElements;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Questionable.QuestPathGenerator;

public static class RoslynShortcuts
{
    public static IEnumerable<SyntaxNodeOrToken> SyntaxNodeList(params SyntaxNodeOrToken?[] nodes)
    {
        nodes = nodes.Where(x => x != null && x.Value.RawKind != 0).ToArray();
        if (nodes.Length == 0)
            return [];

        List<SyntaxNodeOrToken> list = new();
        for (int i = 0; i < nodes.Length; ++i)
        {
            if (i > 0)
                list.Add(Token(SyntaxKind.CommaToken));
            list.Add(nodes[i].GetValueOrDefault());
        }

        return list;
    }

    public static ExpressionSyntax LiteralValue<T>(T? value)
    {
        try
        {
            return value switch
            {
                null => LiteralExpression(SyntaxKind.NullLiteralExpression),
                string s => LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(s)),
                bool b => LiteralExpression(b ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression),
                short i16 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(i16)),
                int i32 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(i32)),
                byte u8 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(u8)),
                ushort u16 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(u16)),
                uint u32 => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(u32)),
                float f => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(f)),
                QuestStep step => step.ToExpressionSyntax(),
                QuestId questId => questId.ToExpressionSyntax(),
                SatisfactionSupplyNpcId satisfactionSupplyNpcId => satisfactionSupplyNpcId.ToExpressionSyntax(),
                AlliedSocietyDailyId alliedSocietyDailyId => alliedSocietyDailyId.ToExpressionSyntax(),
                UnlockLinkId unlockLinkId => unlockLinkId.ToExpressionSyntax(),
                Vector3 vector => vector.ToExpressionSyntax(),
                AethernetShortcut aethernetShortcut => aethernetShortcut.ToExpressionSyntax(),
                ChatMessage chatMessage => chatMessage.ToExpressionSyntax(),
                DialogueChoice dialogueChoice => dialogueChoice.ToExpressionSyntax(),
                JumpDestination jumpDestination => jumpDestination.ToExpressionSyntax(),
                ExcelRef excelRef => excelRef.ToExpressionSyntax(),
                PurchaseMenu purchaseMenu => purchaseMenu.ToExpressionSyntax(),
                ComplexCombatData complexCombatData => complexCombatData.ToExpressionSyntax(),
                QuestWorkValue questWorkValue => questWorkValue.ToExpressionSyntax(),
                List<QuestWorkValue> list => list.ToExpressionSyntax(), // TODO fix in AssignmentList
                DutyOptions dutyOptions => dutyOptions.ToExpressionSyntax(),
                SinglePlayerDutyOptions dutyOptions => dutyOptions.ToExpressionSyntax(),
                SkipConditions skipConditions => skipConditions.ToExpressionSyntax(),
                SkipStepConditions skipStepConditions => skipStepConditions.ToExpressionSyntax(),
                SkipItemConditions skipItemCondition => skipItemCondition.ToExpressionSyntax(),
                NearPositionCondition nearPositionCondition => nearPositionCondition.ToExpressionSyntax(),
                SkipAetheryteCondition skipAetheryteCondition => skipAetheryteCondition.ToExpressionSyntax(),
                GatheredItem gatheredItem => gatheredItem.ToExpressionSyntax(),
                GatheringNodeGroup nodeGroup => nodeGroup.ToExpressionSyntax(),
                GatheringNode nodeLocation => nodeLocation.ToExpressionSyntax(),
                GatheringLocation location => location.ToExpressionSyntax(),
                CombatItemUse combatItemUse => combatItemUse.ToExpressionSyntax(),
                not null when value.GetType().IsEnum => MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(value.GetType().Name), IdentifierName(value.GetType().GetEnumName(value)!)),
                _ => throw new Exception($"Unsupported data type {value.GetType()} = {value}")
            };
        }
        catch (Exception e)
        {
            throw new Exception($"Unable to handle literal [{value}]: {e}", e);
        }
    }

    public static AssignmentExpressionSyntax? Assignment<T>(string name, T? value, T? defaultValue)
    {
        try
        {
            if (value == null && defaultValue == null)
                return null;

            if (value != null && defaultValue != null && value.Equals(defaultValue))
                return null;

            return AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                IdentifierName(name),
                LiteralValue(value));
        }
        catch (Exception e)
        {
            throw new Exception($"Unable to handle assignment [{name}]: {e}", e);
        }
    }

    public static AssignmentExpressionSyntax? AssignmentList<T>(string name, IEnumerable<T>? value)
    {
        try
        {
            if (value == null)
                return null;

            IEnumerable<T> list = value.ToList();
            if (!list.Any())
                return null;

            return AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                IdentifierName(name),
                CollectionExpression(
                    SeparatedList<CollectionElementSyntax>(
                        SyntaxNodeList(list.Select(x => ExpressionElement(
                            LiteralValue(x)).AsSyntaxNodeOrToken()).ToArray())
                    )));
        }
        catch (Exception e)
        {
            throw new Exception($"Unable to handle list [{name}]: {e}", e);
        }
    }

    public static SyntaxNodeOrToken? AsSyntaxNodeOrToken(this SyntaxNode? node)
    {
        if (node == null)
            return null;

        return node;
    }
}
