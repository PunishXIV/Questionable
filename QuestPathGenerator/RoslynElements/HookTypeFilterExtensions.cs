using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Questionable.Model.Questing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Questionable.QuestPathGenerator.RoslynElements;

internal static class HookTypeFilterExtensions
{
    public static ExpressionSyntax ToExpressionSyntax(this HookTypeFilter filter)
    {
        if (filter.IsAllHooksets)
        {
            return MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName(nameof(HookTypeFilter)),
                IdentifierName(nameof(HookTypeFilter.AllHooksets)));
        }

        return InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(nameof(HookTypeFilter)),
                    IdentifierName(nameof(HookTypeFilter.From))))
            .WithArgumentList(
                ArgumentList(
                    SingletonSeparatedList(
                        Argument(filter.Hookset!.ToExpressionSyntax()))));
    }
}
