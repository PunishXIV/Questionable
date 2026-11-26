// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @alydevs

using System.IO;
using System.Reflection;

namespace Questionable.Tests.TestData;

public sealed class EmbeddedQuest
{
    public EmbeddedQuest(string manifestName)
    {
        ManifestName = manifestName;
        ShortName = manifestName[(manifestName.LastIndexOf('/') + 1)..]
            .Replace(".json", "", System.StringComparison.Ordinal);
    }

    public string ManifestName { get; }

    public string ShortName { get; }

    public Stream OpenStream() =>
        typeof(EmbeddedQuest).Assembly.GetManifestResourceStream(ManifestName)
        ?? throw new InvalidDataException($"Embedded resource '{ManifestName}' not found");

    public override string ToString() => ShortName;
}
