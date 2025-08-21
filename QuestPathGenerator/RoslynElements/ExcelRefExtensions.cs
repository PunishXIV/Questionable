using System;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Questionable.QuestPathGenerator.RoslynShortcuts;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class ExcelRefExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this ExcelRef excelRef)
    {
        if (excelRef.Type == ExcelRef.EType.Key)
            return ObjectCreationExpression(
                    IdentifierName(nameof(ExcelRef)))
                .WithArgumentList(
                    ArgumentList(
                        SingletonSeparatedList(
                            Argument(LiteralValue(excelRef.AsKey())))));
        else if (excelRef.Type == ExcelRef.EType.RowId)
            return ObjectCreationExpression(
                    IdentifierName(nameof(ExcelRef)))
                .WithArgumentList(
                    ArgumentList(
                        SingletonSeparatedList(
                            Argument(LiteralValue(excelRef.AsRowId())))));
        else
            throw new Exception($"Unsupported ExcelRef type {excelRef.Type}");
    }
}
