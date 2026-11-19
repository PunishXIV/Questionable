using Sheets = Lumina.Excel.Sheets;
namespace Questionable.Windows.Utils;

internal sealed record RedoCache(Sheets.QuestRedoChapterUI ChapterUi, List<Sheets.Quest> Quests)
{
    public Sheets.QuestRedoChapterUI ChapterUi = ChapterUi;
    public List<Sheets.Quest> Quests = Quests;
}
