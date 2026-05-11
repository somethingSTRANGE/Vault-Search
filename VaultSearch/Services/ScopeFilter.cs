namespace VaultSearch.Services;

/// <summary>
/// Applies folder include/exclude rules from a scope.txt file.
/// Rules are processed top-to-bottom; last match wins.
/// Format:
///   -* (exclude all)
///   +10 Projects (include folder)
///   +20 Areas (include folder)
///   # comment
/// If no scope file exists, all files are included.
/// </summary>
public class ScopeFilter
{
    private readonly List<(bool Include, string Pattern)> _rules = new();

    public bool HasRules => _rules.Count > 0;

    public static ScopeFilter Load(string scopePath)
    {
        var filter = new ScopeFilter();
        if (!File.Exists(scopePath)) return filter;

        foreach (var line in File.ReadAllLines(scopePath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            if (trimmed.StartsWith('-'))
                filter._rules.Add((false, trimmed[1..].Trim()));
            else if (trimmed.StartsWith('+'))
                filter._rules.Add((true, trimmed[1..].Trim()));
        }
        return filter;
    }

    public bool IsIncluded(string vaultPath, string absoluteFilePath)
    {
        if (!HasRules) return true;

        var rel = Path.GetRelativePath(vaultPath, absoluteFilePath).Replace('\\', '/');
        var included = true;

        foreach (var (include, pattern) in _rules)
        {
            if (Matches(rel, pattern))
                included = include;
        }
        return included;
    }

    /// <summary>
    /// Returns explicit directory paths to pass to ripgrep.
    /// When include rules restrict the scope, returns those directories directly
    /// (which is faster than globs). Falls back to the vault root when unrestricted.
    /// </summary>
    public IReadOnlyList<string> GetSearchRoots(string vaultPath)
    {
        if (!HasRules) return [vaultPath];

        var roots = _rules
            .Where(r => r.Include && r.Pattern != "*")
            .Select(r => Path.Combine(vaultPath, r.Pattern.Replace('/', Path.DirectorySeparatorChar)))
            .Where(Directory.Exists)
            .ToList();

        return roots.Count > 0 ? roots : [vaultPath];
    }

    private static bool Matches(string relativePath, string pattern)
    {
        if (pattern == "*") return true;
        var normPattern = pattern.Replace('\\', '/').TrimEnd('/');
        return relativePath.Equals(normPattern, StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(normPattern + "/", StringComparison.OrdinalIgnoreCase);
    }
}
