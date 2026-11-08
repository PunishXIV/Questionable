using System.Collections;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Questionable.Utils;

internal static class TroubleshootingReport
{
    // Discord's limit is 2000; the rest is left for the player's description.
    internal const int MaxLength = 1700;
    private const int MaxLineLength = 400;
    private const int MaxLogLineLength = 160;
    private const int MaxSettingsLength = 400;
    private const string Prompt = "What went wrong: (describe it here)\n```text\n";
    private const string End = "\n```";

    internal sealed record Input
    {
        public IReadOnlyList<string> Facts { get; init; } = [];
        public IReadOnlyList<string> Settings { get; init; } = [];
        public IReadOnlyList<RecentLog.Entry> Log { get; init; } = [];
        public IReadOnlyList<string> PrivateTexts { get; init; } = [];
        public DateTime NowUtc { get; init; }
    }

    internal static string Build(Input input)
    {
        string Clean(string text, int maxLength) => Shorten(FirstLine(Scrub(text, input.PrivateTexts)), maxLength);

        StringBuilder body = new();
        foreach (string fact in input.Facts)
            body.Append(Clean(fact, MaxLineLength)).Append('\n');

        if (input.Settings.Count > 0)
            body.Append(DescribeSettings(input.Settings.Select(x => Scrub(x, input.PrivateTexts)).ToList())).Append('\n');

        int budget = MaxLength - Prompt.Length - End.Length;
        string head = Shorten(body.ToString().TrimEnd(), budget);

        const string logTitle = "\n\nRecent log:";
        List<string> lines = [];
        int used = head.Length + logTitle.Length;
        foreach (RecentLog.Entry entry in input.Log.Reverse())
        {
            string line = $"-{Age(input.NowUtc - entry.Time)} {LevelTag(entry.Level)} {Clean(entry.Message, MaxLogLineLength)}";
            if (used + line.Length + 1 > budget)
                break;
            lines.Insert(0, line);
            used += line.Length + 1;
        }

        string log = lines.Count == 0 ? "" : logTitle + "\n" + string.Join('\n', lines);
        return Prompt + head + log + End;
    }

    internal static string? DescribeFailure(NavigationFailureSnapshot? failure, DateTimeOffset now)
    {
        if (failure == null)
            return null;

        string?[] parts =
        [
            $"Last navigation failure ({Age(now - failure.TimestampUtc)} ago): {failure.Reason}",
            failure.Message,
            failure.Quest == null ? null : $"quest {failure.Quest}",
            failure.Task == null ? null : $"task {failure.Task}",
            $"territory {failure.TerritoryId}",
            $"from {failure.PlayerPosition} to {failure.Destination}",
            failure.DataId == null ? null : $"target {failure.DataId}",
            failure.Fly == true ? "flying" : null,
            failure.FloorPoint == null ? null : $"floor {failure.FloorPoint}",
            failure.NearestReachablePoint == null ? null : $"reachable {failure.NearestReachablePoint}",
            failure.NavmeshVersion == null ? null : $"vnav {failure.NavmeshVersion}",
            failure.InnerExceptionMessage == null ? null : $"{failure.InnerExceptionType}: {failure.InnerExceptionMessage}",
        ];
        return string.Join(" | ", parts.OfType<string>());
    }

    // Free text is never copied: a player can put anything in a text setting and the report goes to a public channel.
    internal static List<string> ChangedSettings(Configuration configuration)
    {
        Configuration defaults = new();
        (string Name, object Current, object Default)[] sections =
        [
            ("General", configuration.General, defaults.General),
            ("Stop", configuration.Stop, defaults.Stop),
            ("Duties", configuration.Duties, defaults.Duties),
            ("QuestBattles", configuration.SinglePlayerDuties, defaults.SinglePlayerDuties),
            ("Notifications", configuration.Notifications, defaults.Notifications),
            ("Advanced", configuration.Advanced, defaults.Advanced),
        ];

        List<string> changed = [];
        foreach ((string name, object current, object defaultSection) in sections)
        {
            foreach (PropertyInfo property in current.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object? value = property.GetValue(current);
                if (!JToken.DeepEquals(ToToken(value), ToToken(property.GetValue(defaultSection))))
                    changed.Add($"{name}.{property.Name}={DescribeValue(value)}");
            }
        }

        return changed;
    }

    private static JToken ToToken(object? value) => value == null ? JValue.CreateNull() : JToken.FromObject(value);

    private static string DescribeValue(object? value) => value switch
    {
        null => "none",
        bool b => b ? "on" : "off",
        string => "(text)",
        IEnumerable items => $"({items.Cast<object?>().Count()} items)",
        _ when value.GetType().IsPrimitive || value is Enum => string.Create(CultureInfo.InvariantCulture, $"{value}"),
        _ => "(changed)",
    };

    private static string DescribeSettings(List<string> settings)
    {
        const string title = "Settings changed: ";
        StringBuilder text = new(title);
        for (int i = 0; i < settings.Count; ++i)
        {
            string more = $"+{settings.Count - i} more";
            if (text.Length + settings[i].Length + 2 + more.Length > MaxSettingsLength)
                return text.Append(more).ToString();
            text.Append(settings[i]);
            if (i < settings.Count - 1)
                text.Append(", ");
        }

        return text.ToString();
    }

    private static string Scrub(string text, IReadOnlyList<string> privateTexts)
    {
        foreach (string privateText in privateTexts)
        {
            if (privateText.Length > 0)
                text = text.Replace(privateText, "(hidden)", StringComparison.OrdinalIgnoreCase);
        }

        return text.Replace("```", "'''", StringComparison.Ordinal);
    }

    private static string FirstLine(string text)
    {
        int lineEnd = text.IndexOfAny(['\r', '\n']);
        return lineEnd < 0 ? text : text[..lineEnd];
    }

    private static string Shorten(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";

    private static string Age(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;
        if (age.TotalMinutes < 1)
            return string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalSeconds}s");
        if (age.TotalHours < 1)
            return string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalMinutes}m");
        return string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalHours}h");
    }

    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        _ => "CRT",
    };
}
