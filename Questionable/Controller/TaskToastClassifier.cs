using System.Text.RegularExpressions;
using Lumina.Excel.Sheets;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class TaskToastClassifier
{
    private readonly string? _eventCanceledText;
    private readonly Regex _actionCanceledRegex;
    private readonly string? _statusBlockedText;

    public TaskToastClassifier(IDataManager dataManager)
        : this(dataManager.GetString<LogMessage>(1318, x => x.Text),
            dataManager.GetRegex<LogMessage>(1314, x => x.Text)!,
            dataManager.GetString<LogMessage>(7728, x => x.Text))
    {
    }

    internal TaskToastClassifier(string? eventCanceledText, Regex actionCanceledRegex, string? statusBlockedText)
    {
        _eventCanceledText = eventCanceledText;
        _actionCanceledRegex = actionCanceledRegex;
        _statusBlockedText = statusBlockedText;
    }

    public bool IsActionCancellation(string text) => _actionCanceledRegex.IsMatch(text);

    public bool IsEventOrStatusCancellation(string text) =>
        GameStringComparison.AreEqual(_statusBlockedText, text) ||
        GameStringComparison.AreEqual(_eventCanceledText, text);
}
