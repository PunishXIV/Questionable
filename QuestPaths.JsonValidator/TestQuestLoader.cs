using System.Collections;

namespace QuestPaths.JsonValidator;

public sealed class TestQuestLoader : IEnumerable<object[]>
{
    private readonly List<object[]> _data;

    public TestQuestLoader()
    {
        var assembly = typeof(TestQuestLoader).Assembly;
        _data = assembly.GetManifestResourceNames()
            .Where(x => x.StartsWith("QuestPaths/") && x.EndsWith(".json"))
            .Select(x => new object[]{new QuestWrapper(x)})
            .ToList();
    }
    public IEnumerator<object[]> GetEnumerator() => _data.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
