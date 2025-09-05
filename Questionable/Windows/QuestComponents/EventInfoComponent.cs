using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Microsoft.Extensions.Logging;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Questionable.Windows.QuestComponents;

internal sealed class EventInfoComponent
{
    private readonly QuestData _questData;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestFunctions _questFunctions;
    private readonly UiUtils _uiUtils;
    private readonly QuestController _questController;
    private readonly QuestTooltipComponent _questTooltipComponent;
    private readonly Configuration _configuration;

    private List<IQuestInfo> _cachedActiveSeasonalQuests = new();
    private DateTime _cachedAtUtc = DateTime.MinValue;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);
    private readonly ILogger<EventInfoComponent> _logger;
    private static readonly Action<ILogger, int, DateTime, Exception?> _logRefreshedSeasonalQuestCache =
        LoggerMessage.Define<int, DateTime>(
            LogLevel.Debug,
            new EventId(0, nameof(UpdateCacheIfNeeded)),
            "Refreshed seasonal quest cache: {Count} active seasonal quests (UTC now {UtcNow:o})"
        );

    public EventInfoComponent(QuestData questData,
        QuestRegistry questRegistry,
        QuestFunctions questFunctions,
        UiUtils uiUtils,
        QuestController questController,
        QuestTooltipComponent questTooltipComponent,
        Configuration configuration,
        ILogger<EventInfoComponent> logger)
    {
        _questData = questData;
        _questRegistry = questRegistry;
        _questFunctions = questFunctions;
        _uiUtils = uiUtils;
        _questController = questController;
        _questTooltipComponent = questTooltipComponent;
        _configuration = configuration;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool ShouldDraw
    {
        get
        {
            if (!_configuration.General.ShowIncompleteSeasonalEvents)
                return false;
            UpdateCacheIfNeeded();
            return _cachedActiveSeasonalQuests.Count > 0;
        }
    }

    public void Draw()
    {
        UpdateCacheIfNeeded();

        // Collect active seasonal quests and group them by the folder (event) name if available
        var activeQuests = _cachedActiveSeasonalQuests;
        var groups = activeQuests.GroupBy(q =>
        {
            if (_questRegistry.TryGetQuestFolderName(q.QuestId, out var folder) && !string.IsNullOrEmpty(folder))
                return folder!;
            return q.Name;
        });

        foreach (var group in groups)
        {
            // pick the earliest expiry among group members to show as the group's expiry
            DateTime endsAt = group.Select(q =>
            {
                DateTime? raw = (q as QuestInfo)?.SeasonalQuestExpiry
                               ?? (q is UnlockLinkQuestInfo uli ? uli.QuestExpiry : (DateTime?)null);
                if (raw is DateTime d)
                    return NormalizeExpiry(d);

                return DateTime.MaxValue;
            })
            .DefaultIfEmpty(DateTime.MaxValue)
            .Min();

            // If all unlock-link quests in the group share the same patch, surface it for display
            var patches = group
                .Select(q => (q as UnlockLinkQuestInfo)?.Patch)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();
            string? patch = patches.Count == 1 ? patches[0] : null;

            var eventQuest = new EventQuest(group.Key, group.Select(q => q.QuestId).ToList(), endsAt, patch);
            DrawEventQuest(eventQuest);
        }
    }

    private void DrawEventQuest(EventQuest eventQuest)
    {
        string displayName = eventQuest.Name;
        if (!string.IsNullOrEmpty(eventQuest.Patch))
            displayName = $"{displayName} [{eventQuest.Patch}]";

        if (eventQuest.EndsAtUtc != DateTime.MaxValue)
        {
            var remaining = eventQuest.EndsAtUtc - DateTime.UtcNow;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;

            string time = FormatRemainingTime(remaining);
            ImGui.Text($"{displayName} ({time})");
        }
        else
            ImGui.Text(displayName);

        List<ElementId> startableQuests = eventQuest.QuestIds.Where(x =>
                _questRegistry.IsKnownQuest(x) &&
                _questFunctions.IsReadyToAcceptQuest(x) &&
                x != _questController.StartedQuest?.Quest.Id &&
                x != _questController.NextQuest?.Quest.Id)
            .ToList();
        foreach (var questId in eventQuest.QuestIds)
        {
            if (_questFunctions.IsQuestComplete(questId))
                continue;

            using (ImRaii.PushId($"##EventQuestSelection{questId}"))
            {
                string questName = _questData.GetQuestInfo(questId).Name;
                if (startableQuests.Contains(questId) &&
                    _questRegistry.TryGetQuest(questId, out Quest? quest))
                {
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Play))
                    {
                        _questController.SetNextQuest(quest);
                        _questController.Start("SeasonalEventSelection");
                    }

                    bool hovered = ImGui.IsItemHovered();

                    ImGui.SameLine();
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(questName);
                    hovered |= ImGui.IsItemHovered();

                    if (hovered)
                        _questTooltipComponent.Draw(quest.Info);
                }
                else
                {
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX());

                    var style = _uiUtils.GetQuestStyle(questId);
                    if (_uiUtils.ChecklistItem(questName, style.Color, style.Icon, ImGui.GetStyle().FramePadding.X))
                        _questTooltipComponent.Draw(_questData.GetQuestInfo(questId));
                }
            }
        }
    }

    public IEnumerable<ElementId> GetCurrentlyActiveEventQuests()
    {
        UpdateCacheIfNeeded();

        return _cachedActiveSeasonalQuests
            .Where(q =>
            {
                if ((q as QuestInfo)?.SeasonalQuestExpiry is DateTime expiry)
                {
                    DateTime expiryUtc = NormalizeExpiry(expiry);
                    if (expiryUtc >= DateTime.UtcNow)
                        return true;
                }

                if (q is UnlockLinkQuestInfo uli && uli.QuestExpiry is DateTime uExpiry)
                {
                    DateTime expiryUtc = NormalizeExpiry(uExpiry);
                    if (expiryUtc >= DateTime.UtcNow)
                        return true;
                }

                return false;
            })
            .Select(q => q.QuestId)
            .Where(ShouldShowQuest);
    }

    private bool ShouldShowQuest(ElementId elementId) => !_questFunctions.IsQuestComplete(elementId) &&
                                                         !_questFunctions.IsQuestUnobtainable(elementId);

    private sealed record EventQuest(string Name, List<ElementId> QuestIds, DateTime EndsAtUtc, string? Patch);

    // Replaced original GetActiveSeasonalQuests with cached implementation that refreshes occasionally.
    private IEnumerable<IQuestInfo> GetActiveSeasonalQuestsNoCache()
    {
        // Only refresh the minimal set: iterate over known quest ids once per refresh interval.
        var allQuestIds = _questRegistry.GetAllQuestIds();
        foreach (var questId in allQuestIds)
        {
            if (!_questData.TryGetQuestInfo(questId, out var q))
                continue;

            if (_questFunctions.IsQuestComplete(q.QuestId) || _questFunctions.IsQuestUnobtainable(q.QuestId))
                continue;

            if (q is UnlockLinkQuestInfo uli)
            {
                if (uli.QuestExpiry is DateTime uExpiry)
                {
                    DateTime expiryUtc = NormalizeExpiry(uExpiry);
                    if (expiryUtc > DateTime.UtcNow)
                    {
                        yield return q;
                        continue;
                    }
                }

                // no future expiry -> skip
                continue;
            }

            if (q is QuestInfo qi)
            {
                if (qi.SeasonalQuestExpiry is DateTime expiry)
                {
                    DateTime expiryUtc = NormalizeExpiry(expiry);
                    if (expiryUtc > DateTime.UtcNow)
                    {
                        yield return q;
                        continue;
                    }
                }

                if (qi.IsSeasonalQuest && qi.SeasonalQuestExpiry is null)
                {
                    yield return q;
                    continue;
                }
            }
        }
    }

    private void UpdateCacheIfNeeded()
    {
        if (DateTime.UtcNow - _cachedAtUtc < _cacheDuration)
            return;

        _cachedActiveSeasonalQuests = GetActiveSeasonalQuestsNoCache().ToList();
        _cachedAtUtc = DateTime.UtcNow;

        _logRefreshedSeasonalQuestCache(_logger, _cachedActiveSeasonalQuests.Count, _cachedAtUtc, null);

        var groups = _cachedActiveSeasonalQuests.GroupBy(q =>
        {
            if (_questRegistry.TryGetQuestFolderName(q.QuestId, out var folder) && !string.IsNullOrEmpty(folder))
                return folder!;
            return q.Name;
        });

        foreach (var group in groups)
        {
            DateTime endsAt = group.Select(q =>
            {
                DateTime? raw = (q as QuestInfo)?.SeasonalQuestExpiry
                               ?? (q is UnlockLinkQuestInfo uli ? uli.QuestExpiry : (DateTime?)null);
                if (raw is DateTime d)
                    return NormalizeExpiry(d);

                return DateTime.MaxValue;
            })
            .DefaultIfEmpty(DateTime.MaxValue)
            .Min();

            var patches = group
                .Select(q => (q as UnlockLinkQuestInfo)?.Patch)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();
            string? patch = patches.Count == 1 ? patches[0] : null;

            if (endsAt != DateTime.MaxValue)
                _logger.LogInformation("Seasonal event '{Name}' ends at {Expiry:o} UTC (patch={Patch})", group.Key, endsAt, patch ?? "n/a");
            else
                _logger.LogInformation("Seasonal event '{Name}' has no expiry (patch={Patch})", group.Key, patch ?? "n/a");
        }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    public static DateTime AtDailyReset(DateOnly date)
    {
        // Use 14:59:59 UTC as the daily expiry instant
        return new DateTime(date, new TimeOnly(14, 59, 59), DateTimeKind.Utc);
    }

    // Normalize expiries consistently:
    // - treat date-only (00:00:00) or explicit end-of-day (23:59:59) as AtDailyReset(date)
    // - otherwise convert to UTC preserving the time
    private static DateTime NormalizeExpiry(DateTime d)
    {
        var tod = d.TimeOfDay;
        var endOfDay = new TimeSpan(23, 59, 59);

        if (tod == TimeSpan.Zero || tod == endOfDay)
            return AtDailyReset(DateOnly.FromDateTime(d));

        return d.Kind == DateTimeKind.Utc ? d : d.ToUniversalTime();
    }

    // Format remaining time as "Xd Yh", "Xh Ym" or "Xm"
    private static string FormatRemainingTime(TimeSpan remaining)
    {
        if (remaining.TotalDays >= 1)
        {
            int days = (int)remaining.TotalDays;
            int hours = remaining.Hours;
            return $"{days}d {hours}h";
        }

        if (remaining.TotalHours >= 1)
        {
            int hours = (int)remaining.TotalHours;
            int minutes = remaining.Minutes;
            return $"{hours}h {minutes}m";
        }

        int mins = Math.Max(0, (int)Math.Ceiling(remaining.TotalMinutes));
        return $"{mins}m";
    }
}
