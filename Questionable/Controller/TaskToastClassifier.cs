using System.Text.RegularExpressions;
using Lumina.Excel.Sheets;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed class TaskToastClassifier
{
    private readonly string? _eventCanceledText;
    private readonly Regex _actionCanceledRegex;
    private readonly string? _statusBlockedText;
    private readonly IReadOnlyList<string?> _tooFarTexts;

    public TaskToastClassifier(IDataManager dataManager)
        : this(dataManager.GetString<LogMessage>(1318, x => x.Text),
            dataManager.GetRegex<LogMessage>(1314, x => x.Text)!,
            dataManager.GetString<LogMessage>(7728, x => x.Text),
            [.. new uint[] { 648, 1249, 1310 }.Select(x => dataManager.GetString<LogMessage>(x, y => y.Text))])
    {
    }

    internal TaskToastClassifier(string? eventCanceledText, Regex actionCanceledRegex, string? statusBlockedText,
        IReadOnlyList<string?>? tooFarTexts = null)
    {
        _eventCanceledText = eventCanceledText;
        _actionCanceledRegex = actionCanceledRegex;
        _statusBlockedText = statusBlockedText;
        _tooFarTexts = tooFarTexts ?? [];
    }

    public bool IsTooFarAway(string text) =>
        _tooFarTexts.Any(x => x != null && GameStringComparison.AreEqual(x, text));

    public bool IsActionCancellation(string text) => _actionCanceledRegex.IsMatch(text);

    public bool IsEventOrStatusCancellation(string text) =>
        GameStringComparison.AreEqual(_statusBlockedText, text) ||
        GameStringComparison.AreEqual(_eventCanceledText, text);
}
