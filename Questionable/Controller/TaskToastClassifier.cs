using System.Text.RegularExpressions;
using Lumina.Excel.Sheets;

namespace Questionable.Controller;

/// <summary>Recognizes localized task interruptions without accessing game state while classifying a toast.</summary>
[RegisterSingleton]
internal sealed class TaskToastClassifier
{
    private readonly string? _eventCanceledText;
    private readonly Regex _actionCanceledText;
    private readonly string? _cantExecuteDueToStatusText;

    public TaskToastClassifier(IDataManager dataManager)
        : this(DataManagerAdapter.GetString<LogMessage>(dataManager, 1318, x => x.Text),
            DataManagerAdapter.GetRegex<LogMessage>(dataManager, 1314, x => x.Text)!,
            DataManagerAdapter.GetString<LogMessage>(dataManager, 7728, x => x.Text))
    {
    }

    internal TaskToastClassifier(string? eventCanceledText, Regex actionCanceledText, string? cantExecuteDueToStatusText)
    {
        _eventCanceledText = eventCanceledText;
        _actionCanceledText = actionCanceledText;
        _cantExecuteDueToStatusText = cantExecuteDueToStatusText;
    }

    public bool IsActionCancellation(string text) => _actionCanceledText.IsMatch(text);

    public bool IsEventOrStatusCancellation(string text) =>
        GameStringComparison.AreEqual(_cantExecuteDueToStatusText, text) ||
        GameStringComparison.AreEqual(_eventCanceledText, text);
}
