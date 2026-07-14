using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Dalamud.Game.Text;
using ECommons.ExcelServices;
using Questionable.Model;
using Questionable.Model.Common;
using Questionable.Model.Questing;
namespace Questionable.Domain;

internal interface IQuestInfo
{
    public ElementId QuestId { get; }
    public string Name { get; }
    public string BaseName => Name;
    public uint IssuerDataId { get; }
    public bool IsRepeatable { get; }
    public ImmutableList<PreviousQuestInfo> PreviousQuests { get; }
    public EQuestJoin PreviousQuestJoin { get; }
    public ushort Level { get; }
    public EAlliedSociety AlliedSociety { get; }
    public uint? JournalGenre { get; }
    public ushort SortKey { get; }
    public bool IsMainScenarioQuest { get; }
    public IReadOnlyList<Job> ClassJobs { get; }
    public EExpansionVersion Expansion { get; }

    public string SimplifiedName => BaseName
        .Replace(".", "", StringComparison.Ordinal)
        .Replace("*", "", StringComparison.Ordinal)
        .Replace("\"", "", StringComparison.Ordinal)
        .Replace("/", "", StringComparison.Ordinal)
        .Replace("\\", "", StringComparison.Ordinal)
        .Replace("<", "", StringComparison.Ordinal)
        .Replace(">", "", StringComparison.Ordinal)
        .Replace("|", "", StringComparison.Ordinal)
        .Replace(":", "", StringComparison.Ordinal)
        .Replace("?", "", StringComparison.Ordinal)
        .TrimStart(SeIconChar.QuestSync.ToIconChar(), SeIconChar.QuestRepeatable.ToIconChar(), ' ');
}
