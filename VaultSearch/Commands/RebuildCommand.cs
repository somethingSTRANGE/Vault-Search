using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using VaultSearch.Config;
using VaultSearch.Services;

namespace VaultSearch.Commands;

public sealed class RebuildCommand : Command<RebuildCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--vault")]
        [Description("Path to the vault folder. Defaults to current directory.")]
        public string? VaultPath { get; init; }

        [CommandOption("--data")]
        [Description("Path to the data directory (DB and config). Defaults to auto-detected location.")]
        public string? DataPath { get; init; }
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
        var tokenCount = 0;

        AnsiConsole.Status().Start("Rebuilding word list...", ctx =>
        {
            ctx.Spinner(Spinner.Known.Dots);
            wordListService.Rebuild(stopwords, scope);
            tokenCount = wordListService.GetAllTokens().Count;
        });

        AnsiConsole.MarkupLine($"[green]Rebuild complete.[/] {tokenCount:N0} tokens indexed.");
        return 0;
    }
}
