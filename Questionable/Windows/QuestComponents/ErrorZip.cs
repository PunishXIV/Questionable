namespace Questionable.Windows.QuestComponents;

// Players often export hours after the error, when the log no longer covers it.
[RegisterSingleton]
internal sealed class ErrorZip : IDisposable
{
    private readonly QuestController _questController;
    private readonly QuickAccessComponent _quickAccess;

    public ErrorZip(QuestController questController, QuickAccessComponent quickAccess)
    {
        _questController = questController;
        _quickAccess = quickAccess;
        _questController.ErrorShown += OnError;
    }

    private void OnError(object? sender, ProblemEventArgs e) => _quickAccess.Export(auto: true, e.Kind);

    public void Dispose() => _questController.ErrorShown -= OnError;
}
