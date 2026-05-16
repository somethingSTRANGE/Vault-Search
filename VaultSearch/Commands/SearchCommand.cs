using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using VaultSearch.Config;
using VaultSearch.Services;

namespace VaultSearch.Commands;

public sealed class SearchCommand : Command<SearchCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<query>")]
        [Description("Search query (space-separated terms).")]
        public string Query { get; init; } = "";

        [CommandOption("--top|-n")]
        [Description("Maximum number of results to return.")]
        [DefaultValue(10)]
        public int Top { get; init; } = 10;

        [CommandOption("--vault")]
        [Description("Path to the vault folder. Defaults to current directory.")]
        public string? VaultPath { get; init; }

        [CommandOption("--data")]
        [Description("Path to the data directory (DB and config). Defaults to auto-detected location.")]
        public string? DataPath { get; init; }

        [CommandOption("--rebuild")]
        [Description("Force a rebuild of the word list before searching.")]
        public bool ForceRebuild { get; init; }

        [CommandOption("--scope|-s")]
        [Description("Named search scope preset. Loads scope-<name>.txt from the config directory.")]
        public string? ScopeName { get; init; }

        [CommandOption("--type")]
        [Description("Filter results to files where frontmatter 'type' matches this value.")]
        public string? TypeFilter { get; init; }

        [CommandOption("--property")]
        [Description("Filter results to files that have this frontmatter property set (any value).")]
        public string? PropertyFilter { get; init; }

        [CommandOption("--pretty")]
        [Description("Render results as a formatted table instead of plain structured text.")]
        public bool Pretty { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var config = AppConfig.Load();
        var vaultPath = settings.VaultPath ?? Directory.GetCurrentDirectory();
        var dataDir = AppConfig.ResolveDataDir(vaultPath, settings.DataPath);

        if (!Directory.Exists(vaultPath))
        {
            AnsiConsole.MarkupLine($"[red]Vault path does not exist: {Markup.Escape(vaultPath)}[/]");
            return 1;
        }

        AppConfig.EnsureConfigStubs(dataDir);
        var scope = ScopeFilter.Load(AppConfig.ScopePath(dataDir));
        var stopwords = Stopwords.GetAll(AppConfig.StopwordsExtraPath(dataDir));
        var wordListService = new WordListService(vaultPath, AppConfig.DbPath(dataDir));

        AnsiConsole.Status().Start("Checking word list...", ctx =>
        {
            ctx.Spinner(Spinner.Known.Dots);
            if (settings.ForceRebuild || wordListService.IsDirty() || !File.Exists(AppConfig.FmAliasesPath(dataDir)))
            {
                ctx.Status("Rebuilding word list...");
                wordListService.Rebuild(stopwords, scope);
                ctx.Status("Extracting frontmatter aliases...");
                FmAliasExtractor.Extract(vaultPath, scope, stopwords, AppConfig.FmAliasesPath(dataDir));
            }
        });

        ScopeFilter searchScope;
        if (settings.ScopeName is not null)
        {
            var namedScopePath = AppConfig.ScopePath(dataDir, settings.ScopeName);
            if (!File.Exists(namedScopePath))
            {
                AnsiConsole.MarkupLine($"[red]Scope preset '{Markup.Escape(settings.ScopeName)}' not found: {Markup.Escape(namedScopePath)}[/]");
                return 1;
            }
            searchScope = ScopeFilter.Load(namedScopePath);
        }
        else
        {
            searchScope = scope;
        }

        var allTokens = wordListService.GetAllTokens();
        var fuzzyService = new FuzzyMatchService(config.FuzzyThreshold);
        var aliasService = new AliasService(AppConfig.AliasesPath(dataDir), AppConfig.FmAliasesPath(dataDir));
        var ripgrepService = new RipgrepService();
        var searchRoots = searchScope.GetSearchRoots(vaultPath);

        var queryTokens = settings.Query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Phrase patterns: full query + all bigrams (for multi-token queries).
        // ripgrep --fixed-strings matches literal space-containing strings line-by-line.
        var phrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (queryTokens.Length > 1)
        {
            phrases.Add(string.Join(' ', queryTokens));
            for (var i = 0; i < queryTokens.Length - 1; i++)
                phrases.Add($"{queryTokens[i]} {queryTokens[i + 1]}");
        }

        // Exact terms: literal query tokens + alias expansions.
        var exactTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in queryTokens)
        {
            exactTerms.Add(token);
            foreach (var alias in aliasService.Expand(token))
                exactTerms.Add(alias);
        }

        // Fuzzy terms: expansions that aren't already exact.
        var fuzzyTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in queryTokens)
            foreach (var match in fuzzyService.ExpandQuery(token, allTokens))
                if (!exactTerms.Contains(match) && !phrases.Contains(match))
                    fuzzyTerms.Add(match);

        var allTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        allTerms.UnionWith(phrases);
        allTerms.UnionWith(exactTerms);
        allTerms.UnionWith(fuzzyTerms);

        var matches = ripgrepService.Search(searchRoots, allTerms);
        if (matches.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No results found.[/]");
            return 0;
        }

        const long PhraseHitScore    = 1000L;
        const long ExactHitScore     = 100L;
        const long FuzzyHitScore     = 10L;
        const long AllTokensBonus    = 500L;
        const long FilenameMultiplier = 5L;

        var scoredAll = matches
            .GroupBy(m => m.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var filePath = g.Key;
                var filename = Path.GetFileNameWithoutExtension(filePath);
                long score = 0;
                bool hasPhraseHit = false;
                var coveredTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var m in g)
                {
                    foreach (var term in m.MatchedTerms)
                    {
                        if (phrases.Contains(term))
                        {
                            score += PhraseHitScore;
                            hasPhraseHit = true;
                        }
                        else if (exactTerms.Contains(term))
                        {
                            score += ExactHitScore;
                            // Track which query tokens this term satisfies.
                            foreach (var qt in queryTokens)
                                if (term.Equals(qt, StringComparison.OrdinalIgnoreCase) ||
                                    aliasService.Expand(qt).Any(a => a.Equals(term, StringComparison.OrdinalIgnoreCase)))
                                    coveredTokens.Add(qt);
                        }
                        else
                            score += FuzzyHitScore;
                    }
                }

                // Bonus when the file contains at least one match for every query token.
                if (queryTokens.Length > 1 && coveredTokens.Count == queryTokens.Length)
                    score += AllTokensBonus;

                // Filename bonus: check if phrase or any token appears in the filename.
                foreach (var phrase in phrases)
                    if (filename.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                        score += PhraseHitScore * FilenameMultiplier;
                foreach (var token in queryTokens)
                    if (filename.Contains(token, StringComparison.OrdinalIgnoreCase))
                        score += ExactHitScore * FilenameMultiplier;

                var hasExactHit = hasPhraseHit || coveredTokens.Count > 0
                    || phrases.Any(p => filename.Contains(p, StringComparison.OrdinalIgnoreCase))
                    || queryTokens.Any(t => filename.Contains(t, StringComparison.OrdinalIgnoreCase));

                var excerpt = g.First().Line;
                return (FilePath: filePath, Score: score, HasExactHit: hasExactHit, Excerpt: excerpt);
            })
            .Where(r => settings.TypeFilter is null ||
                        FmAliasExtractor.ReadProperty(r.FilePath, "type")
                            ?.Equals(settings.TypeFilter, StringComparison.OrdinalIgnoreCase) == true)
            .Where(r => settings.PropertyFilter is null ||
                        FmAliasExtractor.HasProperty(r.FilePath, settings.PropertyFilter))
            .ToList();

        // Exact/phrase results fill slots first; fuzzy-only results backfill any remainder.
        var scored = scoredAll
            .Where(r => r.HasExactHit).OrderByDescending(r => r.Score)
            .Concat(scoredAll.Where(r => !r.HasExactHit).OrderByDescending(r => r.Score))
            .Take(settings.Top)
            .ToList();

        if (settings.Pretty)
        {
            var table = new Table()
                .Border(TableBorder.Simple)
                .AddColumn(new TableColumn("#").RightAligned())
                .AddColumn("File")
                .AddColumn(new TableColumn("Score").RightAligned())
                .AddColumn("Excerpt");

            for (var i = 0; i < scored.Count; i++)
            {
                var (filePath, score, _, excerpt) = scored[i];
                var relativePath = Path.GetRelativePath(vaultPath, filePath);
                var truncated = excerpt.Length > 80 ? excerpt[..77] + "..." : excerpt;
                table.AddRow(
                    $"{i + 1}",
                    Markup.Escape(relativePath),
                    score.ToString(),
                    Markup.Escape(truncated));
            }

            AnsiConsole.Write(table);
        }
        else
        {
            for (var i = 0; i < scored.Count; i++)
            {
                var (filePath, score, _, excerpt) = scored[i];
                var relativePath = Path.GetRelativePath(vaultPath, filePath);
                AnsiConsole.WriteLine($"{i + 1}. {relativePath} (score: {score})");
                AnsiConsole.WriteLine($"   {excerpt}");
                if (i < scored.Count - 1)
                    AnsiConsole.WriteLine();
            }
        }

        return 0;
    }
}
