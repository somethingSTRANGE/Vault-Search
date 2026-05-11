using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace VaultSearch.Config;

public class AppConfig
{
    public string DefaultVaultPath { get; set; } = "";
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

    public static string VaultToolsDir(string vaultPath) =>
        Path.Combine(vaultPath, ".obsidian", "tools", "vaultsearch");

    public static string DbDir(string vaultPath) =>
        Path.Combine(VaultToolsDir(vaultPath), "db");

    public static string DbPath(string vaultPath) =>
        Path.Combine(DbDir(vaultPath), "vaultsearch.db");

    public static string ConfigDir(string vaultPath) =>
        Path.Combine(VaultToolsDir(vaultPath), "config");

    public static string AliasesPath(string vaultPath) =>
        Path.Combine(ConfigDir(vaultPath), "aliases.yaml");

    public static string StopwordsExtraPath(string vaultPath) =>
        Path.Combine(ConfigDir(vaultPath), "stopwords-extra.txt");
}
