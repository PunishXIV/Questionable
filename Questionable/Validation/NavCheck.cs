using Quest = Questionable.Domain.Quest;

namespace Questionable.Validation;

[RegisterSingleton]
internal sealed class NavCheck(
    MappingTheRealmIpc mappingTheRealm,
    TerritoryData territoryData,
    IFramework framework,
    IChatGui chatGui,
    IDalamudPluginInterface pluginInterface,
    ILogger<NavCheck> logger) : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private CancellationTokenSource? _cancel;

    public void Dispose()
    {
        _cancel?.Cancel();
        _cancel?.Dispose();
    }

    public void Stop()
    {
        if (_cancel == null)
            Print("No nav check is running.");
        else
            _cancel.Cancel();
    }

    public void Start(IReadOnlyList<Quest> quests, string reportName)
    {
        if (_cancel != null)
        {
            Print("A nav check is already running; /qst navcheck stop ends it.");
            return;
        }

        if (!mappingTheRealm.IsReady())
        {
            Print("MappingTheRealm isn't loaded.");
            return;
        }

        // Territory 1 never travels and duty instances are entered through the duty finder.
        var steps = quests
            .SelectMany(quest => quest.AllSteps().Select(x => (Quest: quest, x.Sequence, x.StepId, x.Step)))
            .Where(x => x.Step is { Position: not null, TerritoryId: not 1 } &&
                        !territoryData.IsDutyInstance(x.Step.TerritoryId))
            .ToList();
        if (steps.Count == 0)
        {
            Print("No positioned steps to check.");
            return;
        }

        _cancel = new CancellationTokenSource();
        CancellationToken token = _cancel.Token;
        string path = Path.Combine(pluginInterface.ConfigDirectory.FullName, "navcheck", $"{reportName}.tsv");
        Print($"Checking {steps.Count} steps...");
        _ = Task.Run(async () =>
        {
            try
            {
                List<string> rows = ["Result\tQuest\tSequence\tStep\tType\tTerritory\tPosition"];
                int unreachable = 0;
                foreach (var (quest, sequence, stepId, step) in steps)
                {
                    token.ThrowIfCancellationRequested();
                    string result = await Check(step.TerritoryId, step.Position!.Value, token).ConfigureAwait(false);
                    if (result != "ok")
                        unreachable++;
                    rows.Add(string.Join('\t', result, quest.Id, sequence.Sequence, stepId, step.InteractionType,
                        TerritoryData.GetNameAndId(step.TerritoryId),
                        step.Position.Value.ToString("G6", CultureInfo.InvariantCulture)));
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllLinesAsync(path, rows, token).ConfigureAwait(false);
                await PrintOnFramework($"Nav check done: {steps.Count - unreachable}/{steps.Count} reachable. Report: {path}")
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await PrintOnFramework("Nav check cancelled.").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Nav check failed");
                await PrintOnFramework($"Nav check stopped: {e.Message}").ConfigureAwait(false);
            }
            finally
            {
                await framework.RunOnFrameworkThread(() =>
                {
                    _cancel?.Dispose();
                    _cancel = null;
                }).ConfigureAwait(false);
            }
        }, token);
    }

    private async Task<string> Check(uint territoryId, Vector3 position, CancellationToken token)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(Timeout);
        try
        {
            return await framework.RunOnFrameworkThread(() => mappingTheRealm.CanReach(territoryId, position, timeout.Token))
                .ConfigureAwait(false)
                ? "ok"
                : "unreachable";
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return "timeout";
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            logger.LogDebug(e, "CanReach failed for {Territory} {Position}", territoryId, position);
            return $"error: {e.GetBaseException().Message}";
        }
    }

    private void Print(string message) => chatGui.Print(message, CommandHandler.MessageTag, CommandHandler.TagColor);

    private Task PrintOnFramework(string message) => framework.RunOnFrameworkThread(() => Print(message));
}
