using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using VaultSearch.Config;
using VaultSearch.Services;

namespace VaultSearch.Commands;

public sealed class SpellingsCommand : Command<SpellingsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[token]")]
        [Description("Token to check for similar variants. Omit to audit all high-frequency tokens.")]
        public string? Token { get; init; }

        [CommandOption("--vault")]
        [Description("Path to the vault folder (overrides configured default).")]
        public string? VaultPath { get; init; }

        [CommandOption("--threshold|-t")]
        [Description("Minimum frequency for canonical tokens in audit mode.")]
        public int? FrequencyThreshold { get; init; }
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

        var stopwords = Stopwords.GetAll(AppConfig.StopwordsExtraPath(vaultPath));
        var wordListService = new WordListService(vaultPath);

        AnsiConsole.Status().Start("Checking word list...", ctx =>
        {
            ctx.Spinner(Spinner.Known.Dots);
            if (wordListService.IsDirty())
            {
                ctx.Status("Rebuilding word list...");
                wordListService.Rebuild(stopwords);
            }
        });

        var allTokens = wordListService.GetAllTokens();
        var fuzzyService = new FuzzyMatchService(config.FuzzyThreshold);
        var ripgrepService = new RipgrepService();

        if (settings.Token is not null)
            RunSingleToken(settings.Token, allTokens, fuzzyService, ripgrepService, vaultPath);
        else
            RunAudit(allTokens, fuzzyService, ripgrepService, vaultPath,
                settings.FrequencyThreshold ?? config.SpellingsFrequencyThreshold,
                config.SpellingsMisspellingRatio);

        return 0;
    }

    private static void RunSingleToken(
        string token,
        IReadOnlyList<(string Token, int Frequency)> allTokens,
        FuzzyMatchService fuzzyService,
        RipgrepService ripgrepService,
        string vaultPath)
    {
        var similar = fuzzyService.FindSimilar(token, allTokens, excludeExact: true);

        if (similar.Count == 0)
        {
            AnsiConsole.MarkupLine($"[green]No similar tokens found for '{Markup.Escape(token)}'.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Simple)
            .Title($"Similar to '{Markup.Escape(token)}'")
            .AddColumn("Token")
            .AddColumn(new TableColumn("Score").RightAligned())
            .AddColumn(new TableColumn("Freq").RightAligned())
            .AddColumn("Files");

        foreach (var (similarToken, score, freq) in similar)
        {
            var files = ripgrepService.FilesContaining(vaultPath, similarToken);
            var fileList = FormatFileList(files, vaultPath);
            table.AddRow(
                Markup.Escape(similarToken),
                score.ToString(),
                freq.ToString(),
                Markup.Escape(fileList));
        }

        AnsiConsole.Write(table);
    }

    private static void RunAudit(
        IReadOnlyList<(string Token, int Frequency)> allTokens,
        FuzzyMatchService fuzzyService,
        RipgrepService ripgrepService,
        string vaultPath,
        int frequencyThreshold,
        double misspellingRatio)
    {
        var misspellings = fuzzyService.FindMisspellings(allTokens, frequencyThreshold, misspellingRatio);

        if (misspellings.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]No likely misspellings found.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Simple)
            .Title("Likely Misspellings")
            .AddColumn("Canonical")
            .AddColumn(new TableColumn("Freq").RightAligned())
            .AddColumn("Variant")
            .AddColumn(new TableColumn("Freq").RightAligned())
            .AddColumn("Files");

        foreach (var (canonical, canonicalFreq, variant, variantFreq) in misspellings)
        {
            var files = ripgrepService.FilesContaining(vaultPath, variant);
            var fileList = FormatFileList(files, vaultPath);
            table.AddRow(
                Markup.Escape(canonical),
                canonicalFreq.ToString(),
                Markup.Escape(variant),
                variantFreq.ToString(),
                Markup.Escape(fileList));
        }

        AnsiConsole.Write(table);
    }

    private static string FormatFileList(IReadOnlyList<string> files, string vaultPath)
    {
        if (files.Count == 0) return "-";
        var names = files.Take(3).Select(f => Path.GetRelativePath(vaultPath, f));
        var suffix = files.Count > 3 ? $" +{files.Count - 3}" : "";
        return string.Join(", ", names) + suffix;
    }
}
