using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using VaultSearch.Config;

namespace VaultSearch.Commands;

public sealed class ScopesCommand : Command<ScopesCommand.Settings>
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
        var vaultPath = settings.VaultPath ?? Directory.GetCurrentDirectory();
        var dataDir = AppConfig.ResolveDataDir(vaultPath, settings.DataPath);
        var configDir = Path.Combine(dataDir, "config");

        if (!Directory.Exists(configDir))
        {
            AnsiConsole.MarkupLine("[yellow]No config directory found. Run any command to initialise.[/]");
            return 0;
        }

        var defaultExists = File.Exists(AppConfig.ScopePath(dataDir));
        var presets = Directory.EnumerateFiles(configDir, "scope-*.txt")
            .Select(f => Path.GetFileNameWithoutExtension(f)["scope-".Length..])
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        AnsiConsole.WriteLine($"Config: {configDir}");
        AnsiConsole.WriteLine();

        var defaultStatus = defaultExists ? "" : " (missing)";
        AnsiConsole.WriteLine($"  default  scope.txt{defaultStatus}");

        foreach (var name in presets)
            AnsiConsole.WriteLine($"  {name}  --scope {name}");

        if (presets.Count == 0)
            AnsiConsole.WriteLine("  (no named presets — create scope-<name>.txt to add one)");

        return 0;
    }
}
