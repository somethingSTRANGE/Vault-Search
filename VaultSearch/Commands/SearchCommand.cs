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
        var expandedTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in queryTokens)
        {
            foreach (var alias in aliasService.Expand(token))
                expandedTerms.Add(alias);
            foreach (var match in fuzzyService.ExpandQuery(token, allTokens))
                expandedTerms.Add(match);
        }

        var matches = ripgrepService.Search(searchRoots, expandedTerms);
        if (matches.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No results found.[/]");
            return 0;
        }

        var scored = matches
            .GroupBy(m => m.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => (FilePath: g.Key, MatchCount: g.Count(), Excerpt: g.First().Line))
            .Where(r => settings.TypeFilter is null ||
                        FmAliasExtractor.ReadProperty(r.FilePath, "type")
                            ?.Equals(settings.TypeFilter, StringComparison.OrdinalIgnoreCase) == true)
            .Where(r => settings.PropertyFilter is null ||
                        FmAliasExtractor.HasProperty(r.FilePath, settings.PropertyFilter))
            .OrderByDescending(r => r.MatchCount)
            .Take(settings.Top)
            .ToList();

        var table = new Table()
            .Border(TableBorder.Simple)
            .AddColumn(new TableColumn("#").RightAligned())
            .AddColumn("File")
            .AddColumn(new TableColumn("Matches").RightAligned())
            .AddColumn("Excerpt");

        for (var i = 0; i < scored.Count; i++)
        {
            var (filePath, matchCount, excerpt) = scored[i];
            var relativePath = Path.GetRelativePath(vaultPath, filePath);
            var truncated = excerpt.Length > 80 ? excerpt[..77] + "..." : excerpt;
            table.AddRow(
                $"{i + 1}",
                Markup.Escape(relativePath),
                matchCount.ToString(),
                Markup.Escape(truncated));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
