using System.Diagnostics;
using System.Text.Json;

namespace VaultSearch.Services;

public record RipgrepMatch(string FilePath, int LineNumber, string Line, IReadOnlyList<string> MatchedTerms);

public class RipgrepService
{
    public IReadOnlyList<RipgrepMatch> Search(IReadOnlyList<string> searchRoots, IEnumerable<string> terms)
    {
        var termList = terms.ToList();
        if (termList.Count == 0) return [];

        var args = new List<string> { "--json", "--ignore-case", "--fixed-strings", "--glob", "*.md" };
        foreach (var term in termList) { args.Add("-e"); args.Add(term); }
        foreach (var root in searchRoots) args.Add(root);

        return ParseOutput(RunRg(args));
    }

    public IReadOnlyList<string> FilesContaining(IReadOnlyList<string> searchRoots, string term)
    {
        var args = new List<string>
        {
            "--json", "--ignore-case", "--fixed-strings", "--glob", "*.md", "-e", term
        };
        foreach (var root in searchRoots) args.Add(root);

        return ParseOutput(RunRg(args))
            .Select(m => m.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string RunRg(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo("rg")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException(
                "Failed to start rg. Is ripgrep installed and on PATH?");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    private static IReadOnlyList<RipgrepMatch> ParseOutput(string output)
    {
        var matches = new List<RipgrepMatch>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.GetProperty("type").GetString() != "match") continue;

                var data = root.GetProperty("data");
                var path = data.GetProperty("path").GetProperty("text").GetString() ?? "";
                var lineNum = data.GetProperty("line_number").GetInt32();
                var text = data.GetProperty("lines").GetProperty("text").GetString()?.Trim() ?? "";
                var matchedTerms = new List<string>();
                if (data.TryGetProperty("submatches", out var submatches))
                    foreach (var sm in submatches.EnumerateArray())
                        if (sm.TryGetProperty("match", out var matchProp) &&
                            matchProp.TryGetProperty("text", out var matchText))
                            matchedTerms.Add(matchText.GetString() ?? "");
                matches.Add(new RipgrepMatch(path, lineNum, text, matchedTerms));
            }
            catch (KeyNotFoundException) { }
            catch (JsonException) { }
        }
        return matches;
    }
}
