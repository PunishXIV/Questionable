using Dalamud.Interface.Utility;
using Lumina.Excel.Sheets;
namespace Questionable.Windows.JournalComponents;

[RegisterSingleton]
internal sealed class CustomDeliveryComponent(
    CustomDeliveries deliveries,
    Configuration configuration,
    QuestController questController,
    QuestStarter questStarter,
    SatisfyIpc satisfy,
    IClientState clientState,
    IDataManager dataManager,
    IDalamudPluginInterface pluginInterface)
{
    private List<DeliveryNpc> _rows = [];
    private long _rowsAt;

    public void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("Custom Deliveries"));
        if (!tab)
            return;

        if (!clientState.IsLoggedIn)
        {
            ImGui.TextDisabled(_L("Log in to see your custom deliveries."));
            return;
        }

        List<uint> order = configuration.General.DeliveryNpcs;
        using (ImRaii.Disabled(order.Count == 0 || questController.IsRunning || !satisfy.IsInstalled))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Play, _L("Do deliveries")))
                questStarter.RunCustomDeliveries();
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(!satisfy.IsInstalled
                ? _L("Needs the Satisfy plugin (vsatisfy).")
                : order.Count == 0
                    ? _L("Tick the NPCs to include with the box in front of their name.")
                    : _L("Satisfy does the ticked NPCs from top to bottom until this week's deliveries run out. Same as /qst deliveries."));

        ImGui.SameLine();
        ImGui.TextUnformatted(_LF("Deliveries left this week: {0}/12", CustomDeliveries.WeeklyLeft));

        if (Environment.TickCount64 - _rowsAt >= 1000)
        {
            _rows = deliveries.GetNpcs();
            _rowsAt = Environment.TickCount64;
        }
        List<DeliveryNpc> rows = _rows;

        ImGui.TextDisabled(_L("Deliver: what Satisfy does for that NPC (craft, gather or fish). Amber items are this week's bonus."));

        using ImRaii.TableDisposable table = ImRaii.Table("CustomDeliveries", 9,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingFixedFit);
        if (!table)
            return;

        ImGui.TableSetupColumn(_L("Name"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(_L("Rank"));
        ImGui.TableSetupColumn(_L("Satisfaction"));
        ImGui.TableSetupColumn(_L("Turn-ins"));
        ImGui.TableSetupColumn(_L("Achievement"));
        ImGui.TableSetupColumn(_L("Deliver"));
        ImGui.TableSetupColumn(_L("Craft"));
        ImGui.TableSetupColumn(_L("Gather"));
        ImGui.TableSetupColumn(_L("Fish"));
        ImGui.TableHeadersRow();

        using var disabled = ImRaii.Disabled(questController.IsRunning);
        foreach (DeliveryNpc npc in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawTick(order, npc.RowId);
            ImGui.SameLine();
            if (npc.Unlocked)
                ImGui.TextUnformatted(npc.Name);
            else
                ImGui.TextDisabled(npc.Name);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(npc.Unlocked ? $"{npc.Rank}/5" : "-");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(!npc.Unlocked ? "-" :
                npc.SatisfactionMax == 0 ? _L("Max") : $"{npc.SatisfactionCur}/{npc.SatisfactionMax}");
            Configuration.EDeliveryItem chosen = configuration.General.DeliveryItemFor(npc.RowId);
            ImGui.TableNextColumn();
            if (npc.Unlocked)
            {
                ImGui.TextUnformatted(npc.TurnInsLeft(chosen).ToString(CultureInfo.CurrentCulture));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(_LF("{0} of {1} used this week", npc.Used, npc.Max));
            }

            ImGui.TableNextColumn();
            if (npc.AchievementMax > 0)
            {
                ImGui.TextUnformatted($"{npc.AchievementCur}/{npc.AchievementMax}");
                bool hovered = ImGui.IsItemHovered();
                QstWidgets.ThinProgressBar((float)npc.AchievementCur / npc.AchievementMax, QstTheme.Amber);
                if (hovered && npc.Max > 0)
                    ImGui.SetTooltip(_LF("{0:0.0} weeks to go", (npc.AchievementMax - npc.AchievementCur) / (float)npc.Max));
            }

            ImGui.TableNextColumn();
            DrawChoice(npc.RowId, chosen);

            for (int slot = 0; slot < 3; slot++)
            {
                ImGui.TableNextColumn();
                if (slot < npc.Requests.Length && npc.Requests[slot].ItemId != 0)
                    DrawRequest(npc.Requests[slot], (int)chosen == slot);
            }
        }

    }

    private void DrawTick(List<uint> ticked, uint id)
    {
        bool on = ticked.Contains(id);
        if (!ImGui.Checkbox($"##DeliveryNpc{id}", ref on))
            return;

        if (on)
            ticked.Add(id);
        else
            ticked.Remove(id);
        ticked.Sort();
        pluginInterface.SavePluginConfig(configuration);
    }

    private void DrawChoice(uint npc, Configuration.EDeliveryItem chosen)
    {
        (FontAwesomeIcon Icon, string Name)[] kinds =
            [(FontAwesomeIcon.Hammer, _L("Craft")), (FontAwesomeIcon.Leaf, _L("Gather")), (FontAwesomeIcon.Fish, _L("Fish"))];
        using var id = ImRaii.PushId($"DeliveryChoice{npc}");
        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing,
            new Vector2(1f * ImGuiHelpers.GlobalScale, ImGui.GetStyle().ItemSpacing.Y));
        for (int slot = 0; slot < kinds.Length; slot++)
        {
            if (slot > 0)
                ImGui.SameLine();
            using var slotId = ImRaii.PushId(slot);
            if (QstWidgets.SegmentToggle(kinds[slot].Icon, (int)chosen == slot,
                    _LF("Satisfy delivers the {0} item.", kinds[slot].Name),
                    _LF("Deliver the {0} item instead.", kinds[slot].Name)))
            {
                configuration.General.DeliveryItems[npc] = (Configuration.EDeliveryItem)slot;
                pluginInterface.SavePluginConfig(configuration);
            }
        }
    }

    private void DrawRequest(DeliveryRequest request, bool picked)
    {
        string name = dataManager.GetExcelSheet<Item>().GetRowOrDefault(request.ItemId)?.Name.ToString() ?? request.ItemId.ToString(CultureInfo.InvariantCulture);
        ImGui.TextColored(request.Bonus ? QstTheme.WithAlpha(QstTheme.Amber, picked ? 1f : 0.55f)
            : picked ? QstTheme.Text : QstTheme.TextMuted, name);
        if (request.Bonus && ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Bonus this week"));
    }
}
