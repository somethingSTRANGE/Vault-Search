using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace VaultSearch.Config;

public class AppConfig
{
    public int FuzzyThreshold { get; set; } = 70;
    public int SpellingsFrequencyThreshold { get; set; } = 20;
    public double SpellingsMisspellingRatio { get; set; } = 0.15;
    public int DefaultTopN { get; set; } = 10;

    public static string GlobalConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Strange", "VaultSearch");

    public static string GlobalConfigPath => Path.Combine(GlobalConfigDir, "config.yaml");

    public static AppConfig Load()
    {
        if (!File.Exists(GlobalConfigPath))
            return new AppConfig();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        return deserializer.Deserialize<AppConfig>(File.ReadAllText(GlobalConfigPath));
    }

    public void Save()
    {
        Directory.CreateDirectory(GlobalConfigDir);
        var serializer = new SerializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .Build();
        File.WriteAllText(GlobalConfigPath, serializer.Serialize(this));
    }

    /// <summary>
    /// Resolves the data directory using a tiered strategy:
    /// 1. Explicit --data path if provided
    /// 2. .obsidian/tools/vault-search/ if the vault is an Obsidian vault
    /// 3. .vault-search/ at the vault root otherwise
    /// </summary>
    public static string ResolveDataDir(string vaultPath, string? explicitDataPath)
    {
        if (explicitDataPath is not null) return explicitDataPath;

        return Directory.Exists(Path.Combine(vaultPath, ".obsidian"))
            ? Path.Combine(vaultPath, ".obsidian", "tools", "vault-search")
            : Path.Combine(vaultPath, ".vault-search");
    }

    public static string DbPath(string dataDir) =>
        Path.Combine(dataDir, "db", "vault-search.db");

    public static string AliasesPath(string dataDir) =>
        Path.Combine(dataDir, "config", "aliases.yaml");

    public static string StopwordsExtraPath(string dataDir) =>
        Path.Combine(dataDir, "config", "stopwords-extra.txt");

    public static string ScopePath(string dataDir) =>
        Path.Combine(dataDir, "config", "scope.txt");

    /// <summary>
    /// Creates the config directory and writes commented stub files if they don't already exist.
    /// Called on every command run so the config surface is always visible to the user.
    /// </summary>
    public static void EnsureConfigStubs(string dataDir)
    {
        var configDir = Path.Combine(dataDir, "config");
        Directory.CreateDirectory(configDir);

        WriteStubIfAbsent(AliasesPath(dataDir), """
            # aliases.yaml — semantic query expansion
            # Maps a search token to a list of equivalent terms.
            # All aliases are searched whenever the key token appears in a query.
            #
            # Example:
            # foo:
            #   - foo
            #   - bar
            #   - baz
            """);

        WriteStubIfAbsent(StopwordsExtraPath(dataDir), """
            # stopwords-extra.txt — additional stopwords
            # One word per line. Lines starting with # are ignored.
            # Words listed here are excluded from the word list during rebuild.
            # The built-in list already covers common English words.
            """);

        WriteStubIfAbsent(ScopePath(dataDir), """
            # scope.txt — folder include/exclude rules
            # Rules are applied top-to-bottom; last matching rule wins.
            # With no active rules (all lines commented), all folders are included.
            #
            # Example — index and search only specific folders:
            # -*
            # +Projects
            # +Areas
            """);
    }

    private static void WriteStubIfAbsent(string path, string content)
    {
        if (!File.Exists(path))
            File.WriteAllText(path, content);
    }
}
