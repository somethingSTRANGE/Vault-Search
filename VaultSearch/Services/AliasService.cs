using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace VaultSearch.Services;

public class AliasService
{
    private readonly Dictionary<string, List<string>> _aliases;

    public AliasService(string aliasesPath)
    {
        _aliases = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(aliasesPath)) return;

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();

        var raw = deserializer.Deserialize<Dictionary<string, List<string>>>(
            File.ReadAllText(aliasesPath));
        if (raw is null) return;

        foreach (var (key, values) in raw)
            _aliases[key.ToLowerInvariant()] = values.Select(v => v.ToLowerInvariant()).ToList();
    }

    public IEnumerable<string> Expand(string token)
    {
        token = token.ToLowerInvariant();
        return _aliases.TryGetValue(token, out var aliases) ? aliases : [token];
    }
}
