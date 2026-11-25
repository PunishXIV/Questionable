using System.IO;
using System.Threading;
using Questionable.Controller;
using Questionable.Model;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestRegistryTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("qst-paths-");

    public void Dispose() => _root.Delete(recursive: true);

    private void AddFile(string relativePath)
    {
        string path = Path.Combine(_root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
    }

    [Fact]
    public void ReadProject_ReadsQuestFiles()
    {
        string expansion = ExpansionData.ExpansionFolders.Values.First();
        AddFile(Path.Combine(expansion, "100_First.json"));
        AddFile(Path.Combine(expansion, "Sub", "101_Second.json"));
        AddFile(Path.Combine(expansion, "notes.json"));
        AddFile(Path.Combine("Elsewhere", "102_Third.json"));

        List<QuestRegistry.ParsedQuest> quests = QuestRegistry.ReadProject(_root, CancellationToken.None);

        Assert.Equal(["100", "101"], quests.Select(x => x.QuestId.ToString()).Order());
    }

    [Fact]
    public void ReadProject_StopsWhenCancelled()
    {
        AddFile(Path.Combine(ExpansionData.ExpansionFolders.Values.First(), "100_First.json"));
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => QuestRegistry.ReadProject(_root, cancelled.Token));
    }
}
