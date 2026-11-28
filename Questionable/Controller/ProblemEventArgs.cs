namespace Questionable.Controller;

internal sealed class ProblemEventArgs(EErrorKind kind) : EventArgs
{
    public EErrorKind Kind { get; } = kind;
}
