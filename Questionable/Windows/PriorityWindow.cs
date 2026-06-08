using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Utils;
using Questionable.Windows.Common;
using Questionable.Windows.QuestComponents;
using Questionable.Windows.Utils;
namespace Questionable.Windows;

internal sealed class PriorityWindow : LWindow
{
    private const string ClipboardPrefix = "qst:priority:";
    private const string LegacyClipboardPrefix = "qst:v1:";
    private const char ClipboardSeparator = ';';
    private const string JobQuestsPresetName = "Job Quests";
    private readonly IChatGui _chatGui;

    private readonly Configuration _configuration;
    private readonly IDalamudPluginInterface _pluginInterface;

    private readonly QuestController _questController;
    private readonly QuestData _questData;
    private readonly QuestFunctions _questFunctions;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestSelector _questSelector;
    private readonly QuestTooltipComponent _questTooltipComponent;
    private readonly UiUtils _uiUtils;
    private Dictionary<string, List<ElementId>>? _builtInPresets;
    private ElementId? _draggedItem;
    private Job? _lastKnownJob;
    private string _presetName = string.Empty;
    private string? _selectedPresetName;

    public PriorityWindow(QuestController questController, QuestFunctions questFunctions, QuestSelector questSelector,
        QuestTooltipComponent questTooltipComponent, UiUtils uiUtils, IChatGui chatGui, QuestRegistry questRegistry,
        IDalamudPluginInterface pluginInterface, Configuration configuration, QuestData questData)
        : base("Priority Quests###QuestionableQuestPriority")
    {
        _questController = questController;
        _questFunctions = questFunctions;
        _questSelector = questSelector;
        _questTooltipComponent = questTooltipComponent;
        _uiUtils = uiUtils;
        _chatGui = chatGui;
        _questRegistry = questRegistry;
        _pluginInterface = pluginInterface;
        _configuration = configuration;
        _questData = questData;

        _questSelector.SuggestionPredicate = quest =>
            !quest.Info.IsMainScenarioQuest &&
            !questFunctions.IsQuestUnobtainable(quest.Id) &&
            questController.PriorityManager.Quests.All(x => x.Id != quest.Id);
        _questSelector.DefaultPredicate = quest => questFunctions.IsQuestAccepted(quest.Id);
        _questSelector.QuestSelected = quest => _questController.PriorityManager.Add(quest);

        Size = new Vector2(400, 400);
        SizeCondition = ImGuiCond.Once;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(400, 30),
            MaximumSize = default
        };
    }

    public override unsafe void DrawContent()
    {
        Job currentJob = (Job)(PlayerState.Instance()->CurrentClassJobId);
        if (_lastKnownJob != null && currentJob != _lastKnownJob && _selectedPresetName == JobQuestsPresetName)
            LoadPreset(JobQuestsPresetName);
        _lastKnownJob = currentJob;

        if (ImGui.CollapsingHeader("Explanation"))
        {
            ImGui.TextWrapped(
                "Questionable will generally try to do:");
            ImGui.BulletText("Priority quests added below, in order");
            ImGui.BulletText("'Priority' quests: class quests, ARR primals, ARR raids");
            ImGui.BulletText(
                "Supported quests in your 'To-Do list'\n(quests from your Quest Journal that are always on-screen)");
            ImGui.BulletText("MSQ quest (if available, unless it is marked as 'ignored'\nin your Journal)");
            ImGui.TextWrapped(
                "If you don't have any active MSQ quest and there is no Priority Quest added here, it will always try to pick up the next quest in the MSQ first.");
        }

        DrawPresets();

        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text("Quests to do first:");
        _questSelector.DrawSelection();
        DrawQuestList();

        List<ElementId> clipboardItems = ParseClipboardItems();
        using (ImRaii.Disabled(clipboardItems.Count == 0))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Download, "Import from Clipboard"))
                ImportFromClipboard(clipboardItems);
        }
        ImGui.SameLine();
        using (ImRaii.Disabled(_questController.PriorityManager.IsEmpty))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Upload, "Export to Clipboard"))
                ExportToClipboard();
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Check, "Remove finished Quests"))
                _questController.PriorityManager.RemoveCompleted(_questFunctions.IsQuestComplete);
            ImGui.SameLine();

            using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
            {
                if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Trash, "Clear All"))
                    _questController.PriorityManager.Clear();
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Hold CTRL to enable this button.");
        }
    }

    private void DrawQuestList()
    {
        List<Quest> priorityQuests = [.. _questController.PriorityManager.Quests];
        Quest? itemToRemove = null;
        Quest? itemToAdd = null;
        int indexToAdd = 0;

        float width = ImGui.GetContentRegionAvail().X;
        List<(Vector2 TopLeft, Vector2 BottomRight)> itemPositions = [];

        for (int i = 0; i < priorityQuests.Count; ++i)
        {
            Vector2 topLeft = ImGui.GetCursorScreenPos() +
                              new Vector2(0, -ImGui.GetStyle().ItemSpacing.Y / 2);
            Quest quest = priorityQuests[i];
            using (ImRaii.PushId($"Quest{quest.Id}"))
            {
                (Vector4 Color, FontAwesomeIcon Icon, string Status) = _uiUtils.GetQuestStyle(quest.Id);
                bool hovered;
                using (IDisposable _ = _pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextColored(Color, Icon.ToIconString());
                    hovered = ImGui.IsItemHovered();
                }

                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(quest.Info.Name);
                hovered |= ImGui.IsItemHovered();

                if (hovered)
                    _questTooltipComponent.Draw(quest.Info);

                if (priorityQuests.Count > 1)
                {
                    using (ImRaii.PushFont(UiBuilder.IconFont))
                    {
                        int _pad = 4;
#if DEBUG
                        _pad += 4;
#endif
                        ImGui.SameLine(ImGui.GetContentRegionAvail().X +
                                       ImGui.GetStyle().WindowPadding.X -
                                       ImGui.CalcTextSize(FontAwesomeIcon.ArrowsUpDown.ToIconString()).X -
                                       ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X -
#if DEBUG
                                       ImGui.CalcTextSize(FontAwesomeIcon.Edit.ToIconString()).X -
#endif
                                       ImGui.GetStyle().FramePadding.X * _pad -
                                       ImGui.GetStyle().ItemSpacing.X);
                    }

                    if (_draggedItem == quest.Id)
                    {
                        ImGuiComponentsLocal.IconButton("##Move", FontAwesomeIcon.ArrowsUpDown,
                            ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32(ImGuiCol.ButtonActive)));
                    }
                    else
                        ImGuiComponentsLocal.IconButton("##Move", FontAwesomeIcon.ArrowsUpDown);

                    if (_draggedItem == null && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                        _draggedItem = quest.Id;

                    ImGui.SameLine();
                }
                else
                {
                    using (ImRaii.PushFont(UiBuilder.IconFont))
                    {
                        int _pad = 2;
#if DEBUG
                        _pad += 4;
#endif
                        ImGui.SameLine(ImGui.GetContentRegionAvail().X +
                                       ImGui.GetStyle().WindowPadding.X -
                                       ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X -
#if DEBUG
                                       ImGui.CalcTextSize(FontAwesomeIcon.Edit.ToIconString()).X -
#endif
                                       ImGui.GetStyle().FramePadding.X * _pad);
                    }
                }

#if DEBUG
                if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.Edit))
                    (bool success, string filename) = QuestRegistry.OpenEditor(quest.Info);
                ImGui.SameLine();
#endif

                if (ImGuiComponentsLocal.IconButton($"##Remove{i}", FontAwesomeIcon.Times))
                    itemToRemove = quest;
            }

            Vector2 bottomRight = new(topLeft.X + width,
                ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().ItemSpacing.Y + 2);
            itemPositions.Add((topLeft, bottomRight));
        }

        if (!ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            _draggedItem = null;
        else if (_draggedItem != null)
        {
            Quest draggedItem = priorityQuests.Single(x => x.Id == _draggedItem);
            int oldIndex = priorityQuests.IndexOf(draggedItem);

            (Vector2 topLeft, Vector2 bottomRight) = itemPositions[oldIndex];
            ImGui.GetWindowDrawList().AddRect(topLeft, bottomRight, ImGui.GetColorU32(ImGuiColors.DalamudGrey), 3f,
                ImDrawFlags.RoundCornersAll);

            int newIndex = itemPositions.FindIndex(x => ImGui.IsMouseHoveringRect(x.TopLeft, x.BottomRight, true));
            if (newIndex >= 0 && oldIndex != newIndex)
            {
                itemToAdd = priorityQuests.Single(x => x.Id == _draggedItem);
                indexToAdd = newIndex;
            }
        }

        if (itemToRemove != null)
            _questController.PriorityManager.Remove(itemToRemove);

        if (itemToAdd != null)
            _questController.PriorityManager.Move(priorityQuests.IndexOf(itemToAdd), indexToAdd);
    }

    private static List<ElementId> ParseClipboardItems()
    {
        string clipboardText = ImGui.GetClipboardText().Trim();
        return DecodeQuestPriority(clipboardText);
    }

    public static List<ElementId> DecodeQuestPriority(string clipboardText)
    {
        List<ElementId> clipboardItems = [];
        try
        {
            if (!string.IsNullOrEmpty(clipboardText))
            {
                string? prefixToRemove = null;

                if (clipboardText.StartsWith(ClipboardPrefix, StringComparison.InvariantCulture))
                    prefixToRemove = ClipboardPrefix;
                else if (clipboardText.StartsWith(LegacyClipboardPrefix, StringComparison.InvariantCulture))
                    prefixToRemove = LegacyClipboardPrefix;

                if (prefixToRemove != null)
                {
                    clipboardText = clipboardText.Substring(prefixToRemove.Length);
                    string text = Encoding.UTF8.GetString(Convert.FromBase64String(clipboardText));
                    foreach (string part in text.Split(ClipboardSeparator))
                    {
                        ElementId elementId = ElementId.FromString(part);
                        clipboardItems.Add(elementId);
                    }
                }
            }
        }
        catch (Exception)
        {
            clipboardItems.Clear();
        }

        return clipboardItems;
    }

    public string EncodeQuestPriority()
    {
        return ClipboardPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(
            string.Join(ClipboardSeparator, _questController.PriorityManager.Quests.Select(x => x.Id.ToString()))));
    }

    private void ExportToClipboard()
    {
        string clipboardText = EncodeQuestPriority();
        ImGui.SetClipboardText(clipboardText);
        _chatGui.Print("Copied quests to clipboard.", CommandHandler.MessageTag, CommandHandler.TagColor);
    }

    private void ImportFromClipboard(List<ElementId> questElements) => _questController.PriorityManager.Import(questElements);

    private void DrawPresets()
    {
        if (!ImGui.CollapsingHeader("Presets"))
            return;

        Dictionary<string, List<ElementId>> builtInPresets = GetOrCreateBuiltInPresets();
        Dictionary<string, List<string>> userPresets = _configuration.Priority.Presets;

        string preview = _selectedPresetName ?? "Select a preset...";
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo("##PresetSelection", preview, ImGuiComboFlags.HeightLarge))
        {
            ImGui.TextDisabled("Built-in");
            foreach (string name in builtInPresets.Keys)
            {
                if (ImGui.Selectable(name, _selectedPresetName == name))
                {
                    _selectedPresetName = name;
                    LoadPreset(name);
                }
            }

            if (userPresets.Count > 0)
            {
                ImGui.Separator();
                ImGui.TextDisabled("Custom");
                foreach (string name in userPresets.Keys)
                {
                    if (ImGui.Selectable(name, _selectedPresetName == name))
                    {
                        _selectedPresetName = name;
                        LoadPreset(name);
                    }
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TextColoredWrapped(ImGuiColors.DalamudRed, "Selecting a preset will override your current priority list and activate the preset. " +
            "You can save your current list as a preset by entering a name below and selecting Save.");

        ImGui.Spacing();

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint("##PresetName", "Preset name...", ref _presetName, 128);

        bool nameEmpty = string.IsNullOrWhiteSpace(_presetName);
        bool nameIsBuiltIn = !nameEmpty && builtInPresets.ContainsKey(_presetName.Trim());
        bool nameExists = !nameEmpty && userPresets.ContainsKey(_presetName.Trim());
        bool noQuests = _questController.PriorityManager.IsEmpty;

        using (ImRaii.Disabled(nameEmpty || nameIsBuiltIn || noQuests || (nameExists && !ImGui.IsKeyDown(ImGuiKey.ModCtrl))))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Save, "Save Preset"))
            {
                SavePreset(_presetName.Trim());
                _presetName = string.Empty;
            }
        }

        bool isUserPreset = _selectedPresetName != null && userPresets.ContainsKey(_selectedPresetName);
        if (isUserPreset)
        {
            ImGui.SameLine();
            using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
            {
                if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Trash, "Delete Preset"))
                {
                    userPresets.Remove(_selectedPresetName!);
                    _selectedPresetName = null;
                    SaveConfig();
                }
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Hold CTRL to enable this button.");
        }

        if (nameIsBuiltIn)
            ImGui.TextColored(ImGuiColors.DalamudRed, "Cannot overwrite a built-in preset.");
        else if (nameExists)
            ImGui.TextColored(ImGuiColors.DalamudYellow, "Hold CTRL to overwrite existing preset.");
    }

    //TODO Add all jobs for all role quests
    private Dictionary<string, List<ElementId>> GetOrCreateBuiltInPresets()
    {
        if (_builtInPresets != null)
            return _builtInPresets;

        List<ElementId> gilList = ((ushort[])[
            835, 903, 916, 918, 919, 920, 929, 928, 930, 931, 932, 945, 1010, 1011, 1015, 1017, 1019, 1553, 1021, 1023,
            1024, 1025, 1026, 1027, 1028, 1029, 1030, 1031, 1032, 1033, 1034, 1035
        ]).FromNumericListOfQuests();
        List<ElementId> postARRUnlocks = ((ushort[])[
            // don't add DoH/DoL unlocks to this
            // Features
            160, 1463, // materia
            699, 3017, // dyes, glams
            1210, // aesthetician
            1211, // treasure maps
            1431, // challenge log
            1432, 1433, 1434, // retainers
            1212, 1213, 1214, // housing districts
            1563, 1564, 1565, // hunts
            4644, // island sanc visit
            3759, // new game+
            5187, // free fantasia
            // Duties
            94, // sastacha hard
            697, 1410, // halatali
            764, 96, // qarn
            870, 431, // wanderer's palace
            921, // cutter's cry
            1128, 1129, 1130, // dzemael gc
            1131, 1132, 1133, // aurum gc
            1135, // amdapor (requires aurum)
            430, // amdapor hard
            1208, 1209, // pharos sirius
            1215, // haukke hard
            1216, // copperbell hard
            1389, // lost city of amdapor
            1411, // brayflox hard
            1524, // tamtara hard
            1525, // stone vigil hard
            1526, // hullbreaker isle
            2248, // hullbreaker hard
            1556, // palace of the dead
        ]).FromNumericListOfQuests();
        _builtInPresets = new()
        {
            [JobQuestsPresetName] = [],
            ["ARR Hard Mode Primals"] = QuestData.HardModePrimals.Cast<ElementId>().ToList(),
            ["Crystal Tower Raids"] = QuestData.CrystalTowerQuests.Cast<ElementId>().ToList(),
            ["Aether Currents: Heavensward"] = GetAetherCurrentQuests(397, 398, 399, 400, 401),
            ["Aether Currents: Stormblood"] = GetAetherCurrentQuests(612, 613, 614, 620, 621, 622),
            ["Aether Currents: Shadowbringers"] = GetAetherCurrentQuests(813, 814, 815, 816, 817, 818),
            ["Aether Currents: Endwalker"] = GetAetherCurrentQuests(956, 957, 958, 959, 960, 961),
            ["Aether Currents: Dawntrail"] = GetAetherCurrentQuests(1187, 1188, 1189, 1190, 1191, 1192),
            ["Role Quests: Tank"] = _questData.GetRoleQuests(Job.PLD).Select(x => x.QuestId).ToList(),
            ["Role Quests: Healer"] = _questData.GetRoleQuests(Job.WHM).Select(x => x.QuestId).ToList(),
            ["Role Quests: Melee DPS"] = _questData.GetRoleQuests(Job.MNK).Select(x => x.QuestId).ToList(),
            ["Role Quests: Physical Ranged"] = _questData.GetRoleQuests(Job.BRD).Select(x => x.QuestId).ToList(),
            ["Role Quests: Caster"] = _questData.GetRoleQuests(Job.BLM).Select(x => x.QuestId).ToList(),
            ["Gil (set TextAdvance to prefer Gil sacks)"] = gilList,
            ["Post-ARR unlocks"] = postARRUnlocks,
        };

        return _builtInPresets;
    }

    private static List<ElementId> GetAetherCurrentQuests(params uint[] territories)
    {
        return territories
            .Where(QuestData.AetherCurrentQuestsByTerritory.ContainsKey)
            .SelectMany(t => QuestData.AetherCurrentQuestsByTerritory[t])
            .Cast<ElementId>()
            .ToList();
    }

    private void LoadPreset(string name)
    {
        _questController.PriorityManager.Clear();

        if (name == JobQuestsPresetName)
        {
            _questController.PriorityManager.Import(GetCurrentJobQuests());
            return;
        }

        Dictionary<string, List<ElementId>> builtInPresets = GetOrCreateBuiltInPresets();
        if (builtInPresets.TryGetValue(name, out List<ElementId>? questIds))
        {
            _questController.PriorityManager.Import(questIds);
        }
        else if (_configuration.Priority.Presets.TryGetValue(name, out List<string>? questIdStrings))
        {
            List<ElementId> ids = [];
            foreach (string s in questIdStrings)
            {
                if (ElementId.TryFromString(s, out ElementId? id) && id != null)
                    ids.Add(id);
            }

            _questController.PriorityManager.Import(ids);
        }
    }

    private void SavePreset(string name)
    {
        List<string> questIds = _questController.PriorityManager.Quests
            .Select(q => q.Id.ToString())
            .ToList();
        _configuration.Priority.Presets[name] = questIds;
        _selectedPresetName = name;
        SaveConfig();
    }

    private unsafe List<ElementId> GetCurrentJobQuests()
    {
        Job currentJob = (Job)(PlayerState.Instance()->CurrentClassJobId);
        if (currentJob == Job.ADV)
            return [];

        return _questRegistry.GetKnownClassJobQuests(currentJob, false)
            .Select(x => x.QuestId)
            .ToList();
    }

    private void SaveConfig() => _pluginInterface.SavePluginConfig(_configuration);
}
