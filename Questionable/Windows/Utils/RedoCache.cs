using Sheets = Lumina.Excel.Sheets;
namespace Questionable.Windows.Utils;

internal sealed record RedoCache(Sheets.QuestRedoChapterUI Chapter, List<Sheets.Quest> Quests)
{
    public Sheets.QuestRedoChapterUI Chapter = Chapter;
    public List<Sheets.Quest> Quests = Quests;
}
