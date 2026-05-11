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
        [Description("Path to the vault folder (overrides configured default).")]
        public string? VaultPath { get; init; }

        [CommandOption("--rebuild")]
        [Description("Force a rebuild of the word list before searching.")]
        public bool ForceRebuild { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var config = AppConfig.Load();
        var vaultPath = settings.VaultPath ?? config.DefaultVaultPath;

        if (string.IsNullOrWhiteSpace(vaultPath))
        {
            AnsiConsole.MarkupLine("[red]No vault path specified. Set DefaultVaultPath in config or use --vault.[/]");
            return 1;
        }

        if (!Directory.Exists(vaultPath))
        {
            AnsiConsole.MarkupLine($"[red]Vault path does not exist: {Markup.Escape(vaultPath)}[/]");
            return 1;
        }

        var stopwords = Stopwords.GetAll(AppConfig.StopwordsExtraPath(vaultPath));
        var wordListService = new WordListService(vaultPath);

        AnsiConsole.Status().Start("Checking word list...", ctx =>
        {
            ctx.Spinner(Spinner.Known.Dots);
            if (settings.ForceRebuild || wordListService.IsDirty())
            {
                ctx.Status("Rebuilding word list...");
                wordListService.Rebuild(stopwords);
            }
        });

        var allTokens = wordListService.GetAllTokens();
        var fuzzyService = new FuzzyMatchService(config.FuzzyThreshold);
        var aliasService = new AliasService(AppConfig.AliasesPath(vaultPath));
        var ripgrepService = new RipgrepService();

        var queryTokens = settings.Query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var expandedTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in queryTokens)
        {
            foreach (var alias in aliasService.Expand(token))
                expandedTerms.Add(alias);
            foreach (var match in fuzzyService.ExpandQuery(token, allTokens))
                expandedTerms.Add(match);
        }

        var matches = ripgrepService.Search(vaultPath, expandedTerms);
        if (matches.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No results found.[/]");
            return 0;
        }

        var scored = matches
            .GroupBy(m => m.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var excerpt = g.First().Line;
                return (FilePath: g.Key, MatchCount: g.Count(), Excerpt: excerpt);
            })
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
