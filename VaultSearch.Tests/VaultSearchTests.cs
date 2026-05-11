using VaultSearch.Config;
using VaultSearch.Services;

namespace VaultSearch.Tests;

[TestFixture]
public class WordListServiceTests
{
    private string _vaultPath = "";

    [SetUp]
    public void SetUp()
    {
        _vaultPath = Path.Combine(Path.GetTempPath(), "VaultSearchTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_vaultPath);
        Directory.CreateDirectory(Path.Combine(_vaultPath, ".obsidian", "tools", "vaultsearch", "db"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_vaultPath))
            Directory.Delete(_vaultPath, recursive: true);
    }

    [Test]
    public void Rebuild_IndexesTokensFromFileContent()
    {
        File.WriteAllText(Path.Combine(_vaultPath, "kazimir.md"), "Kazimir walked into the clinic.");
        var svc = new WordListService(_vaultPath);
        svc.Rebuild(Stopwords.GetAll());

        var tokens = svc.GetAllTokens().Select(t => t.Token).ToList();
        Assert.That(tokens, Contains.Item("kazimir"));
        Assert.That(tokens, Contains.Item("clinic"));
    }

    [Test]
    public void Rebuild_ExcludesStopwords()
    {
        File.WriteAllText(Path.Combine(_vaultPath, "test.md"), "The warden walked into the room.");
        var svc = new WordListService(_vaultPath);
        svc.Rebuild(Stopwords.GetAll());

        var tokens = svc.GetAllTokens().Select(t => t.Token).ToList();
        Assert.That(tokens, Does.Not.Contain("the"));
        Assert.That(tokens, Contains.Item("warden"));
    }

    [Test]
    public void Rebuild_IndexesFilenames()
    {
        File.WriteAllText(Path.Combine(_vaultPath, "Kazimir Volkov.md"), "Some content.");
        var svc = new WordListService(_vaultPath);
        svc.Rebuild(Stopwords.GetAll());

        var tokens = svc.GetAllTokens().Select(t => t.Token).ToList();
        Assert.That(tokens, Contains.Item("kazimir"));
        Assert.That(tokens, Contains.Item("volkov"));
    }

    [Test]
    public void IsDirty_ReturnsFalseImmediatelyAfterRebuild()
    {
        File.WriteAllText(Path.Combine(_vaultPath, "test.md"), "Some content.");
        var svc = new WordListService(_vaultPath);
        svc.Rebuild(Stopwords.GetAll());

        Assert.That(svc.IsDirty(), Is.False);
    }

    [Test]
    public void IsDirty_ReturnsTrueWhenFileIsNewer()
    {
        var mdPath = Path.Combine(_vaultPath, "test.md");
        File.WriteAllText(mdPath, "Some content.");
        var svc = new WordListService(_vaultPath);
        svc.Rebuild(Stopwords.GetAll());

        File.SetLastWriteTimeUtc(mdPath, DateTime.UtcNow.AddSeconds(1));
        Assert.That(svc.IsDirty(), Is.True);
    }

    [Test]
    public void Rebuild_TokenizesHyphenatedIdentifiers()
    {
        File.WriteAllText(Path.Combine(_vaultPath, "weapons.md"), "The type-66 drone circled overhead.");
        var svc = new WordListService(_vaultPath);
        svc.Rebuild(Stopwords.GetAll());

        var tokens = svc.GetAllTokens().Select(t => t.Token).ToList();
        Assert.That(tokens, Contains.Item("type-66"));
    }
}

[TestFixture]
public class FuzzyMatchServiceTests
{
    private static IReadOnlyList<(string Token, int Frequency)> MakeWordList(params string[] tokens) =>
        tokens.Select(t => (t, 10)).ToList();

    [Test]
    public void ExpandQuery_ReturnsExactMatch()
    {
        var svc = new FuzzyMatchService(threshold: 70);
        var wordList = MakeWordList("kazimir", "quadra", "warden");

        var results = svc.ExpandQuery("kazimir", wordList);

        Assert.That(results, Contains.Item("kazimir"));
    }

    [Test]
    public void ExpandQuery_ReturnsFuzzyMatch()
    {
        var svc = new FuzzyMatchService(threshold: 70);
        var wordList = MakeWordList("kazimir", "quadra", "quedra");

        var results = svc.ExpandQuery("quadra", wordList);

        Assert.That(results, Contains.Item("quadra"));
        Assert.That(results, Contains.Item("quedra"));
    }

    [Test]
    public void FindSimilar_ExcludesExactWhenFlagSet()
    {
        var svc = new FuzzyMatchService(threshold: 70);
        var wordList = MakeWordList("quadra", "quedra", "quaadra");

        var results = svc.FindSimilar("quadra", wordList, excludeExact: true);

        Assert.That(results.Select(r => r.Token), Does.Not.Contain("quadra"));
        Assert.That(results.Select(r => r.Token), Contains.Item("quedra"));
    }

    [Test]
    public void FindMisspellings_FlagsRareVariantsOfFrequentTokens()
    {
        var wordList = new List<(string Token, int Frequency)>
        {
            ("kazimir", 100),
            ("kaziimir", 2),
            ("unrelated", 50)
        };
        var svc = new FuzzyMatchService(threshold: 70);

        var results = svc.FindMisspellings(wordList, frequencyThreshold: 20, misspellingRatio: 0.15);

        Assert.That(results, Has.Count.GreaterThan(0));
        Assert.That(results.Any(r => r.Canonical == "kazimir" && r.Similar == "kaziimir"), Is.True);
        Assert.That(results.Any(r => r.Similar == "unrelated"), Is.False);
    }
}
