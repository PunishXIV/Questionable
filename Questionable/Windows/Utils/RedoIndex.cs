using ECommons.MathHelpers;
using Lumina.Excel.Sheets;
namespace Questionable.Windows.Utils;

internal sealed record RedoIndex(QuestRedoChapterUI Chapter, int Index)
{
    public QuestRedoChapterUI Chapter = Chapter;
    public int Index = Index;
    public int SimplifiedIndex
    {
        get
        {
            int index = Index + 1;
            if (Chapter.RowId.Equals(1)) // ARR part 1
            {
                // handling for citystate starts numbering
                if (index.InRange(22, 43)) // gridania
                    index -= 21;
                if (index.InRange(43, 65)) // uldah
                    index -= 42;
                if (index == 65) // call of the sea limsa/gridania
                    index = 22;
                if (index == 66) // call of the sea uldah
                    index = 23;
            }
            return index;
        }
    }

    public override string ToString()
    {
        return $"{Chapter.ChapterName} (#{SimplifiedIndex})";
    }
}