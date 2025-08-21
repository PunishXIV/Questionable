namespace QuestPaths.JsonValidator;

public class QuestWrapper
{
    public QuestWrapper(string manifestName)
    {
        ManifestName = manifestName;
        ShortName = ManifestName.Split('/').Last().Replace(".json", "", StringComparison.InvariantCulture);
    }

    public string ManifestName { get; }
    public string ShortName { get; }
    public Stream AsStream() => typeof(QuestWrapper).Assembly.GetManifestResourceStream(ManifestName)!;

    public override string ToString() => ShortName;
}
