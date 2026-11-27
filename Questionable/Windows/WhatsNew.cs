namespace Questionable.Windows;

// Shown once per update. Add new entries at the end, with the version that ships them.
internal static class WhatsNew
{
    internal sealed record Entry(Version Version, Func<string> Title, Func<string> Text, Toggle? Toggle = null);

    // Edits the same setting as the config window, so profiles behave the same way.
    internal sealed record Toggle(Func<string> Label, Func<Configuration, bool> Get, Action<Configuration, bool> Set);

    private static readonly Version Rewrite = new(16, 0, 0, 0);

    internal static string Current { get; } = typeof(WhatsNew).Assembly.GetName().Version!.ToString(4);

    internal static IReadOnlyList<Entry> Entries { get; } =
    [
        new(Rewrite,
            () => _L("Reward mail is claimed for you"),
            () => _L("Between quests, Questionable walks to a nearby delivery moogle and claims waiting Mog Station and reward mail through Mogmail. Older versions saved this as off before it had a setting, so it was turned on."),
            new(() => _L("Claim Mog Station and reward mail"),
                x => x.General.ClaimMail, (x, value) => x.General.ClaimMail = value)),
        new(Rewrite,
            () => _L("Reward items are no longer opened for you"),
            () => _L("Coffers, minions, emotes and other redeemable items now stay in your inventory unless you turn this on."),
            new(() => _L("Automatically open redeemable items (coffers, minions, emotes, etc.)"),
                x => x.Advanced.AutoRedeemRewardItems, (x, value) => x.Advanced.AutoRedeemRewardItems = value)),
        new(Rewrite,
            () => _L("Step retry was turned off"),
            () => _L("Some saved settings still had the old step refresh on, so it was turned off. Turn it on to retry movement and simple interactions when nothing has changed for a while."),
            new(() => _L("Retry the current step when stuck"),
                x => x.General.AutoStepRefreshEnabled, (x, value) => x.General.AutoStepRefreshEnabled = value)),
        new(Rewrite,
            () => _L("Stop commands and problem stops"),
            () => _L("What Questionable does after it stops (travel, commands and the action after that) can now skip problem stops. Your earlier behaviour was kept."),
            new(() => _L("Do this after a problem stop too (stuck, too many deaths, missing path)"),
                x => x.Stop.RunAfterProblemStop, (x, value) => x.Stop.RunAfterProblemStop = value)),
    ];

    // No saved version means the user had Questionable before this window existed: show everything.
    internal static IReadOnlyList<Entry> Unseen(IReadOnlyList<Entry> entries, string? seen) =>
        Version.TryParse(seen, out Version? last)
            ? entries.Where(x => x.Version > last).ToList()
            : entries;
}
