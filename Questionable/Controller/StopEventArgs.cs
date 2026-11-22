namespace Questionable.Controller;

internal sealed class StopEventArgs(string kind, string reason) : EventArgs
{
    public string Kind { get; } = kind;
    public string Reason { get; } = reason;
}
