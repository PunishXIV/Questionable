using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using Microsoft.Extensions.Logging;
using Questionable.Model;
using Questionable.Utils;

namespace Questionable.Controller.Steps.Shared;
internal static class LogQuestCompletion
{
    private readonly static string LogPath = Path.Combine(Svc.PluginInterface.ConfigDirectory.FullName, "QuestCompletionLog.json");

    internal sealed record Task(Quest? Quest) : ITask
    {
        public override string ToString() => $"LogQuestCompletion({Quest?.Id.Value.ToString(CultureInfo.InvariantCulture) ?? "???"})";
    }

    internal sealed class LogQuestCompletionExecutor
    (
        IClientState clientState,
        IObjectTable objectTable,
        IDalamudPluginInterface pluginInterface,
        ILogger<LogQuestCompletionExecutor> logger) : TaskExecutor<Task>
    {
        protected override bool Start()
        {
            if (objectTable[0] == null || !clientState.IsLoggedIn || Task.Quest == null)
            {
                throw new TaskException("Player is not logged in or quest is null");
            }

            LogQuestCompletionAction();
            return true;
        }

        public override ETaskResult Update() => ETaskResult.TaskComplete;

        public void LogQuestCompletionAction()
        {
            if (Task.Quest == null)
                return;
            logger.LogInformation($"Logging quest completion: {Task.Quest?.Id.Value}");
            string path = Path.Combine(pluginInterface.ConfigDirectory.FullName, "QuestCompletionLog.json");
            List<QuestCompletion> data = ReadQuestCompletions();
            data.Add(new()
            {
                Quest = $"{Task.Quest?.Id}_{Task.Quest?.Info.SimplifiedName}",
                LastChecked = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
            WriteQuestCompletions(data);
        }

        public override bool ShouldInterruptOnDamage() => false;
    }

    public sealed class QuestCompletion
    {
        public required string Quest { get; init; }
        public required string LastChecked { get; init; }
    }

    internal static List<QuestCompletion> ReadQuestCompletions()
    {
        List<QuestCompletion> data = [];
        if (File.Exists(LogPath))
        {
            using FileStream readStream = new(LogPath, FileMode.Open, FileAccess.Read);
            JsonNode? root = JsonNode.Parse(readStream);
            data = root.Deserialize<List<QuestCompletion>>() ?? data;
        }
        return data;
    }

    internal static void WriteQuestCompletions(List<QuestCompletion> questCompletions)
    {
        JsonNode? node = JsonSerializer.SerializeToNode(questCompletions, JsonOptions.Default)!;
        using FileStream writeStream = File.Create(LogPath);
        using Utf8JsonWriter writer = new(writeStream, new()
        {
            Encoder = JsonOptions.Default.Encoder,
            Indented = JsonOptions.Default.WriteIndented
        });
        node.WriteTo(writer, JsonOptions.Default);

    }
}
