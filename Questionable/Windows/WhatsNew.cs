namespace Questionable.Windows;

// Shown once per update. Add new entries at the end, with the version that ships them.
internal static class WhatsNew
{
    internal sealed record Entry(Version Version, Func<string> Title, Func<string> Text, Toggle? Toggle = null);

    // Edits the same setting as the config window, so profiles behave the same way.
    internal sealed record Toggle(Func<string> Label, Func<Configuration, bool> Get, Action<Configuration, bool> Set);

    private static readonly Version Rewrite = new(16, 0, 0, 0);
    private static readonly Version Added = new(16, 0, 0, 3);

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
        new(Added,
            () => _L("Free job gear is collected for you"),
            () => _L("Between quests, Questionable fetches the free level 80, 89 and 99 job gear once its shop is unlocked and it beats what you wear, then equips your best gear."),
            new(() => _L("Collect free job gear"),
                x => x.General.CollectJobGear, (x, value) => x.General.CollectJobGear = value)),
        new(Added,
            () => _L("Gear is repaired for you"),
            () => _L("Between quests, gear below the set condition is repaired with Dark Matter when one of your crafters can, otherwise at a mender for gil."),
            new(() => _L("Repair gear"),
                x => x.General.RepairGear, (x, value) => x.General.RepairGear = value)),
        new(Added,
            () => _L("The Hall of the Novice is run for you"),
            () => _L("Between quests, Questionable runs the Hall of the Novice through AutoDuty for the Neophyte's Ring once its exercises are unlocked."),
            new(() => _L("Run the Hall of the Novice for the Neophyte's Ring"),
                x => x.General.RunNoviceHall, (x, value) => x.General.RunNoviceHall = value)),
        new(Added,
            () => _L("Quests waiting on your own duties are set aside"),
            () => _L("A quest that needs a duty you run yourself is put aside, and Questionable carries on with other quests."),
            new(() => _L("Set aside quests waiting on a duty you run yourself"),
                x => x.Duties.SetAsideManualDuties, (x, value) => x.Duties.SetAsideManualDuties = value)),
        new(Added,
            () => _L("Failed quest battles are retried"),
            () => _L("When a quest battle run by Boss Mod fails, Questionable starts it again."),
            new(() => _L("Retry a failed quest battle"),
                x => x.SinglePlayerDuties.RetryOnFailure, (x, value) => x.SinglePlayerDuties.RetryOnFailure = value)),
        new(Added,
            () => _L("Help fix quest paths"),
            () => _L("Questionable can send whether each quest's path worked, or where and why it stopped, so everyone sees how often a path works. When a quest errors, it also uploads the troubleshooting zip (logs and settings, your character names removed) and gives you a report code. Never your account or IP address. This also applies if you already share."),
            new(() => _L("Share quest results to help fix paths"),
                x => x.Reports.Share == true, (x, value) => x.Reports.Share = value)),
    ];

    // No saved version means the user had Questionable before this window existed: show everything.
    internal static IReadOnlyList<Entry> Unseen(IReadOnlyList<Entry> entries, string? seen) =>
        Version.TryParse(seen, out Version? last)
            ? entries.Where(x => x.Version > last).ToList()
            : entries;
}
