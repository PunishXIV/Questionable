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
    private readonly DragOrder<uint> _drag = new();

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
                    : _L("Satisfy does the ticked NPCs in list order until this week's deliveries run out. Same as /qst deliveries."));

        ImGui.SameLine();
        ImGui.TextUnformatted(_LF("Deliveries left this week: {0}/12", CustomDeliveries.WeeklyLeft));

        List<DeliveryNpc> npcs = deliveries.GetNpcs();
        List<DeliveryNpc> rows =
        [
            .. order.SelectMany(id => npcs.Where(x => x.RowId == id)),
            .. npcs.Where(x => !order.Contains(x.RowId)),
        ];

        ImGui.TextDisabled(_L("Click the craft, gather or fish item Satisfy should deliver to each NPC."));

        using ImRaii.TableDisposable table = ImRaii.Table("CustomDeliveries", 8,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingFixedFit);
        if (!table)
            return;

        ImGui.TableSetupColumn(_L("Name"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(_L("Rank"));
        ImGui.TableSetupColumn(_L("Satisfaction"));
        ImGui.TableSetupColumn(_L("Turn-ins"));
        ImGui.TableSetupColumn(_L("Achievement"));
        ImGui.TableSetupColumn(_L("Craft"));
        ImGui.TableSetupColumn(_L("Gather"));
        ImGui.TableSetupColumn(_L("Fish"));
        ImGui.TableHeadersRow();

        using var disabled = ImRaii.Disabled(questController.IsRunning);
        foreach (DeliveryNpc npc in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (DrawOrder(order, npc.RowId))
                return;
            ImGui.SameLine();
            int index = order.IndexOf(npc.RowId);
            string name = index >= 0 ? $"{index + 1}. {npc.Name}" : npc.Name;
            if (npc.Unlocked)
                ImGui.TextUnformatted(name);
            else
                ImGui.TextDisabled(name);

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
                using (ImRaii.PushColor(ImGuiCol.PlotHistogram, QstTheme.Amber))
                    ImGui.ProgressBar((float)npc.AchievementCur / npc.AchievementMax,
                        new Vector2(110 * ImGuiHelpers.GlobalScale, 0), $"{npc.AchievementCur}/{npc.AchievementMax}");
                if (ImGui.IsItemHovered() && npc.Max > 0)
                    ImGui.SetTooltip(_LF("{0:0.0} weeks to go", (npc.AchievementMax - npc.AchievementCur) / (float)npc.Max));
            }

            for (int slot = 0; slot < 3; slot++)
            {
                ImGui.TableNextColumn();
                if (slot < npc.Requests.Length && npc.Requests[slot].ItemId != 0)
                    DrawRequest(npc, slot, chosen);
            }
        }

        if (_drag.End(order))
            pluginInterface.SavePluginConfig(configuration);
    }

    // Returns true when the list changed, so the rest of this frame's rows are skipped.
    private bool DrawOrder(List<uint> order, uint id)
    {
        int index = order.IndexOf(id);
        bool ticked = index >= 0;
        if (ImGui.Checkbox($"##DeliveryNpc{id}", ref ticked))
        {
            if (ticked)
                order.Add(id);
            else
                order.Remove(id);
            pluginInterface.SavePluginConfig(configuration);
            return true;
        }

        ImGui.SameLine();
        _drag.Handle(id, enabled: ticked && order.Count > 1);
        return false;
    }

    private void DrawRequest(DeliveryNpc npc, int slot, Configuration.EDeliveryItem chosen)
    {
        DeliveryRequest request = npc.Requests[slot];
        string name = dataManager.GetExcelSheet<Item>().GetRowOrDefault(request.ItemId)?.Name.ToString() ?? request.ItemId.ToString(CultureInfo.InvariantCulture);
        using (ImRaii.PushColor(ImGuiCol.Text, QstTheme.Amber, request.Bonus))
        {
            if (ImGui.Selectable($"{name}##Delivery{npc.RowId}_{slot}", (int)chosen == slot))
            {
                configuration.General.DeliveryItems[npc.RowId] = (Configuration.EDeliveryItem)slot;
                pluginInterface.SavePluginConfig(configuration);
            }
        }

        if (request.Bonus && ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Bonus this week"));
    }
}
