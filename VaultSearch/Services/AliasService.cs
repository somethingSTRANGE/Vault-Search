using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace VaultSearch.Services;

public class AliasService
{
    private readonly Dictionary<string, List<string>> _aliases;

    public AliasService(string aliasesPath, string? fmAliasesPath = null)
    {
        _aliases = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (fmAliasesPath is not null) Merge(fmAliasesPath);
        Merge(aliasesPath);
    }

    private void Merge(string path)
    {
        if (!File.Exists(path)) return;

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();

        var raw = deserializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(path));
        if (raw is null) return;

        foreach (var (key, values) in raw)
        {
            var k = key.ToLowerInvariant();
            var incoming = values.Select(v => v.ToLowerInvariant()).ToList();
            if (!_aliases.TryGetValue(k, out var existing))
                _aliases[k] = incoming;
            else
                existing.AddRange(incoming.Where(v => !existing.Contains(v, StringComparer.OrdinalIgnoreCase)));
        }
    }

    public IEnumerable<string> Expand(string token)
    {
        token = token.ToLowerInvariant();
        return _aliases.TryGetValue(token, out var aliases)
            ? aliases.Prepend(token)
            : [token];
    }
}
