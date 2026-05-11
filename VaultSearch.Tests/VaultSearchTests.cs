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
        Directory.CreateDirectory(Path.Combine(_vaultPath, ".obsidian"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_vaultPath))
            Directory.Delete(_vaultPath, recursive: true);
    }

    private WordListService MakeSvc() =>
        new(_vaultPath, AppConfig.DbPath(AppConfig.ResolveDataDir(_vaultPath, null)));

    private static ScopeFilter NoScope() => ScopeFilter.Load("nonexistent-scope.txt");

    [Test]
    public void Rebuild_IndexesTokensFromFileContent()
    {
        File.WriteAllText(Path.Combine(_vaultPath, "test.md"), "Viktor walked into the clinic.");
        var svc = MakeSvc();
        svc.Rebuild(Stopwords.GetAll(), NoScope());

        var tokens = svc.GetAllTokens().Select(t => t.Token).ToList();
        Assert.That(tokens, Contains.Item("viktor"));
        Assert.That(tokens, Contains.Item("clinic"));
    }

    [Test]
    public void Rebuild_ExcludesStopwords()
    {
        File.WriteAllText(Path.Combine(_vaultPath, "test.md"), "The warden walked into the room.");
        var svc = MakeSvc();
        svc.Rebuild(Stopwords.GetAll(), NoScope());

        var tokens = svc.GetAllTokens().Select(t => t.Token).ToList();
        Assert.That(tokens, Does.Not.Contain("the"));
        Assert.That(tokens, Contains.Item("warden"));
    }

    [Test]
    public void Rebuild_IndexesFilenames()
    {
        File.WriteAllText(Path.Combine(_vaultPath, "Volkov Natasha.md"), "Some content.");
        var svc = MakeSvc();
        svc.Rebuild(Stopwords.GetAll(), NoScope());

        var tokens = svc.GetAllTokens().Select(t => t.Token).ToList();
        Assert.That(tokens, Contains.Item("volkov"));
        Assert.That(tokens, Contains.Item("natasha"));
    }

    [Test]
    public void IsDirty_ReturnsFalseImmediatelyAfterRebuild()
    {
        File.WriteAllText(Path.Combine(_vaultPath, "test.md"), "Some content.");
        var svc = MakeSvc();
        svc.Rebuild(Stopwords.GetAll(), NoScope());

        Assert.That(svc.IsDirty(), Is.False);
    }

    [Test]
    public void IsDirty_ReturnsTrueWhenFileIsNewer()
    {
        var mdPath = Path.Combine(_vaultPath, "test.md");
        File.WriteAllText(mdPath, "Some content.");
        var svc = MakeSvc();
        svc.Rebuild(Stopwords.GetAll(), NoScope());

        File.SetLastWriteTimeUtc(mdPath, DateTime.UtcNow.AddSeconds(1));
        Assert.That(svc.IsDirty(), Is.True);
    }

    [Test]
    public void Rebuild_TokenizesHyphenatedIdentifiers()
    {
        File.WriteAllText(Path.Combine(_vaultPath, "weapons.md"), "The type-66 drone circled overhead.");
        var svc = MakeSvc();
        svc.Rebuild(Stopwords.GetAll(), NoScope());

        var tokens = svc.GetAllTokens().Select(t => t.Token).ToList();
        Assert.That(tokens, Contains.Item("type-66"));
    }

    [Test]
    public void Rebuild_RespectsScope_ExcludesOutOfScopeFiles()
    {
        Directory.CreateDirectory(Path.Combine(_vaultPath, "10 Projects"));
        Directory.CreateDirectory(Path.Combine(_vaultPath, "30 Resources"));
        File.WriteAllText(Path.Combine(_vaultPath, "10 Projects", "in-scope.md"), "InScope token here.");
        File.WriteAllText(Path.Combine(_vaultPath, "30 Resources", "out-scope.md"), "OutScope token here.");

        var scopePath = Path.Combine(_vaultPath, "scope.txt");
        File.WriteAllText(scopePath, "-*\n+10 Projects");
        var scope = ScopeFilter.Load(scopePath);

        var svc = MakeSvc();
        svc.Rebuild(Stopwords.GetAll(), scope);

        var tokens = svc.GetAllTokens().Select(t => t.Token).ToList();
        Assert.That(tokens, Contains.Item("inscope"));
        Assert.That(tokens, Does.Not.Contain("outscope"));
    }
}

[TestFixture]
public class ScopeFilterTests
{
    [Test]
    public void NoRules_IncludesAllFiles()
    {
        var scope = ScopeFilter.Load("nonexistent.txt");
        Assert.That(scope.IsIncluded(@"C:\vault", @"C:\vault\some\file.md"), Is.True);
    }

    [Test]
    public void ExcludeAll_ThenIncludeFolder_IncludesOnlyThatFolder()
    {
        var scopePath = Path.Combine(Path.GetTempPath(), "scope_" + Guid.NewGuid() + ".txt");
        File.WriteAllText(scopePath, "-*\n+10 Projects");
        try
        {
            var scope = ScopeFilter.Load(scopePath);
            Assert.That(scope.IsIncluded(@"C:\vault", @"C:\vault\10 Projects\story.md"), Is.True);
            Assert.That(scope.IsIncluded(@"C:\vault", @"C:\vault\30 Resources\ref.md"), Is.False);
        }
        finally { File.Delete(scopePath); }
    }

    [Test]
    public void ExplicitDataPath_RespectsExplicitOverride()
    {
        var dataDir = AppConfig.ResolveDataDir(@"C:\some\vault", @"C:\explicit\data");
        Assert.That(dataDir, Is.EqualTo(@"C:\explicit\data"));
    }

    [Test]
    public void ResolveDataDir_UsesObsidianPathWhenObsidianFolderExists()
    {
        var vault = Path.Combine(Path.GetTempPath(), "ObsVault_" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(vault, ".obsidian"));
        try
        {
            var dataDir = AppConfig.ResolveDataDir(vault, null);
            Assert.That(dataDir, Does.Contain(".obsidian"));
        }
        finally { Directory.Delete(vault, recursive: true); }
    }

    [Test]
    public void ResolveDataDir_UsesDotVaultSearchForPlainFolder()
    {
        var vault = Path.Combine(Path.GetTempPath(), "PlainVault_" + Guid.NewGuid());
        Directory.CreateDirectory(vault);
        try
        {
            var dataDir = AppConfig.ResolveDataDir(vault, null);
            Assert.That(dataDir, Does.EndWith(".vault-search"));
        }
        finally { Directory.Delete(vault, recursive: true); }
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
        var wordList = MakeWordList("viktor", "quadra", "warden");

        var results = svc.ExpandQuery("viktor", wordList);

        Assert.That(results, Contains.Item("viktor"));
    }

    [Test]
    public void ExpandQuery_ReturnsFuzzyMatch()
    {
        var svc = new FuzzyMatchService(threshold: 70);
        var wordList = MakeWordList("viktor", "quadra", "quedra");

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
            ("viktor", 100),
            ("viiktor", 2),
            ("unrelated", 50)
        };
        var svc = new FuzzyMatchService(threshold: 70);

        var results = svc.FindMisspellings(wordList, frequencyThreshold: 20, misspellingRatio: 0.15);

        Assert.That(results, Has.Count.GreaterThan(0));
        Assert.That(results.Any(r => r.Canonical == "viktor" && r.Similar == "viiktor"), Is.True);
        Assert.That(results.Any(r => r.Similar == "unrelated"), Is.False);
    }
}
