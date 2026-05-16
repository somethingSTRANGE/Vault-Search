using System.Reflection;
using Spectre.Console.Cli;
using VaultSearch.Commands;

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion ?? "dev";

var app = new CommandApp<SearchCommand>();
app.Configure(config =>
{
    config.SetApplicationName("vault-search");
    config.SetApplicationVersion(version);
    config.AddCommand<SpellingsCommand>("spellings")
        .WithDescription("Find similar token variants (likely misspellings) in the vault.");
    config.AddCommand<RebuildCommand>("rebuild")
        .WithDescription("Force a full rebuild of the word list.");
    config.AddCommand<ScopesCommand>("scopes")
        .WithDescription("List available scope presets.");
});
return app.Run(args);
