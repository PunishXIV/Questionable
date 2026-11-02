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
///         found <see cref="GameDataFactAttribute"/> reports the tests as skipped. The
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
        string? sqPack = ResolveSqPackDirectory(
            Environment.GetEnvironmentVariable(GamePathEnvVar), FindInstalledSqPackDirectory);
        if (sqPack == null)
            return;

        try
        {
            GameData = new QuestGameData(sqPack);
            Factory = new QuestPathGeneratorFactory(GameData);
        }
        catch
        {
            GameData?.Dispose();
            throw;
        }
    }

    internal QuestGameData? GameData { get; }
    internal QuestPathGeneratorFactory? Factory { get; }

    public bool Available => GameData != null && Factory != null;

    /// <summary>Fixed author, so generated output never depends on the machine's configuration.</summary>
    public const string Author = "TestBot";

    internal QuestPathAutoGenerator CreateGenerator() =>
        Factory?.Create(Author) ?? throw new InvalidOperationException("No game data available.");

    public void Dispose() => GameData?.Dispose();

    internal static string? ResolveSqPackDirectory(string? configured, Func<string?> discover)
    {
        if (!string.IsNullOrEmpty(configured))
        {
            if (!IsGameRoot(configured))
                throw new InvalidOperationException(
                    $"{GamePathEnvVar} points to an invalid FFXIV installation: '{configured}'. " +
                    "Expected game/sqpack/ffxiv/0a0000.win32.index beneath this directory.");

            return Path.Combine(configured, "game", "sqpack");
        }

        return discover();
    }

    internal static string? FindInstalledSqPackDirectory()
    {
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
