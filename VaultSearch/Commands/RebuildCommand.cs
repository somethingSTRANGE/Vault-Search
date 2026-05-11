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
        [Description("Path to the vault folder (overrides configured default).")]
        public string? VaultPath { get; init; }
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
        var tokenCount = 0;

        AnsiConsole.Status().Start("Rebuilding word list...", ctx =>
        {
            ctx.Spinner(Spinner.Known.Dots);
            wordListService.Rebuild(stopwords);
            tokenCount = wordListService.GetAllTokens().Count;
        });

        AnsiConsole.MarkupLine($"[green]Rebuild complete.[/] {tokenCount:N0} tokens indexed.");
        return 0;
    }
}
