// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using System;
using System.IO;
using System.Text.Json;
using Questionable.AutoGen;
using Questionable.AutoGen.Generation;

namespace Questionable.Tests.AutoGen;

/// <summary>
///     Shared game-data handle for the questpath generator tests.
///     <para>
///         These tests read the installed game's sqpack, which CI does not have. When no installation is
///         found <see cref="Available"/> is false and every test in the collection returns early — the
///         generator is only meaningfully testable against real game data, and a mocked
///         <c>Quest</c>/<c>Level</c>/Lua corpus would test the mock rather than the derivation rules.
///         Set <c>QUESTIONABLE_GAME_PATH</c> to point at a game folder explicitly.
///     </para>
/// </summary>
public sealed class GameDataFixture : IDisposable
{
    internal const string GamePathEnvVar = "QUESTIONABLE_GAME_PATH";

    private static readonly string[] CommonPaths =
    [
        @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn",
        @"C:\Program Files\SquareEnix\FINAL FANTASY XIV - A Realm Reborn",
        @"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY XIV Online",
        @"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY XIV - A Realm Reborn"
    ];

    public GameDataFixture()
    {
        string? sqPack = FindSqPackDirectory();
        if (sqPack == null)
            return;

        try
        {
            GameData = new QuestGameData(sqPack);
            Factory = new QuestPathGeneratorFactory(GameData);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            GameData = null;
            Factory = null;
        }
    }

    public QuestGameData? GameData { get; }
    public QuestPathGeneratorFactory? Factory { get; }

    public bool Available => GameData != null && Factory != null;

    /// <summary>Fixed author, so generated output never depends on the machine's configuration.</summary>
    public const string Author = "TestBot";

    public QuestPathAutoGenerator CreateGenerator() =>
        Factory?.Create(Author) ?? throw new InvalidOperationException("No game data available.");

    public void Dispose() => GameData?.Dispose();

    private static string? FindSqPackDirectory()
    {
        // An explicit path is authoritative: if it is set and wrong, fall back to nothing rather than silently
        // testing against a different installation than the one asked for.
        if (Environment.GetEnvironmentVariable(GamePathEnvVar) is { Length: > 0 } configured)
            return IsGameRoot(configured) ? Path.Combine(configured, "game", "sqpack") : null;

        if (TryFromXivLauncher() is { } launcherPath)
            return launcherPath;

        foreach (string path in CommonPaths)
        {
            if (IsGameRoot(path))
                return Path.Combine(path, "game", "sqpack");
        }

        return null;
    }

    private static bool IsGameRoot(string path) =>
        File.Exists(Path.Combine(path, "game", "sqpack", "ffxiv", "0a0000.win32.index"));

    private static string? TryFromXivLauncher()
    {
        string config = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncher",
            "launcherConfigV3.json");

        if (!File.Exists(config))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(config));
            if (!document.RootElement.TryGetProperty("GamePath", out JsonElement gamePath))
                return null;

            string? root = gamePath.GetString();
            return root != null && IsGameRoot(root) ? Path.Combine(root, "game", "sqpack") : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
