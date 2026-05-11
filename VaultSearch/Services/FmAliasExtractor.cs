using System.Text;
using System.Text.RegularExpressions;
using VaultSearch.Config;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace VaultSearch.Services;

public static class FmAliasExtractor
{
    private static readonly Regex TokenPattern = new(
        @"[A-Za-z0-9](?:[A-Za-z0-9\-']*[A-Za-z0-9])?",
        RegexOptions.Compiled);

    public static void Extract(
        string vaultPath,
        ScopeFilter scope,
        IReadOnlySet<string> stopwords,
        string outputPath)
    {
        var mapping = new SortedDictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(vaultPath, "*.md", SearchOption.AllDirectories)
                     .Where(f => scope.IsIncluded(vaultPath, f)))
        {
            var fmAliases = ReadFrontmatterAliases(file);
            if (fmAliases.Count == 0) continue;

            var keyTokens = Tokenize(Path.GetFileNameWithoutExtension(file))
                .Where(t => t.Length >= 2 && !stopwords.Contains(t))
                .ToList();

            var valueTokens = fmAliases
                .SelectMany(Tokenize)
                .Where(t => t.Length >= 2 && !stopwords.Contains(t))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var key in keyTokens)
            {
                if (!mapping.TryGetValue(key, out var set))
                    mapping[key] = set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var value in valueTokens)
                    if (!value.Equals(key, StringComparison.OrdinalIgnoreCase))
                        set.Add(value);
            }
        }

        foreach (var key in mapping.Keys.Where(k => mapping[k].Count == 0).ToList())
            mapping.Remove(key);

        Write(mapping, outputPath);
    }

    private static List<string> ReadFrontmatterAliases(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            if (reader.ReadLine()?.Trim() != "---") return [];

            var sb = new StringBuilder();
            string? line;
            while ((line = reader.ReadLine()) != null && line.Trim() != "---")
                sb.AppendLine(line);

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(LowerCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var fm = deserializer.Deserialize<Frontmatter>(sb.ToString());
            return fm?.Aliases ?? [];
        }
        catch { return []; }
    }

    private static IEnumerable<string> Tokenize(string text) =>
        TokenPattern.Matches(text).Select(m => m.Value.ToLowerInvariant());

    private static void Write(SortedDictionary<string, SortedSet<string>> mapping, string outputPath)
    {
        if (mapping.Count == 0)
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
            return;
        }

        var data = mapping.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());

        var serializer = new SerializerBuilder()
            .WithNamingConvention(LowerCaseNamingConvention.Instance)
            .Build();

        const string header =
            "# AUTO-GENERATED FROM FRONTMATTER — DO NOT EDIT — CHANGES WILL BE LOST\n" +
            "# Regenerated automatically on every vault-search rebuild.\n\n";

        File.WriteAllText(outputPath, header + serializer.Serialize(data));
    }

    private class Frontmatter
    {
        public List<string>? Aliases { get; set; }
    }
}
