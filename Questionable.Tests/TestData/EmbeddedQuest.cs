// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @alydevs

using System.IO;
using System.Reflection;

namespace Questionable.Tests.TestData;

/// <summary>
/// Wraps a QuestPaths JSON file that has been embedded into the test assembly.
/// The manifest name matches the file's path relative to the repository root
/// (using forward slashes), e.g.
/// <c>QuestPaths/2.x - A Realm Reborn/Side Quests/Thanalan/Ul'dah/396_No Lady Is an Island.json</c>.
/// </summary>
public sealed class EmbeddedQuest
{
    public EmbeddedQuest(string manifestName)
    {
        ManifestName = manifestName;
        ShortName = manifestName[(manifestName.LastIndexOf('/') + 1)..]
            .Replace(".json", "", System.StringComparison.Ordinal);
    }

    /// <summary>Full embedded-resource manifest name.</summary>
    public string ManifestName { get; }

    /// <summary>Filename without directories or extension, used as the theory display name.</summary>
    public string ShortName { get; }

    public Stream OpenStream() =>
        typeof(EmbeddedQuest).Assembly.GetManifestResourceStream(ManifestName)
        ?? throw new InvalidDataException($"Embedded resource '{ManifestName}' not found");

    public override string ToString() => ShortName;
}
