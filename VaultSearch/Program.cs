using Spectre.Console.Cli;
using VaultSearch.Commands;

var app = new CommandApp<SearchCommand>();
app.Configure(config =>
{
    config.SetApplicationName("vault-search");
    config.AddCommand<SpellingsCommand>("spellings")
        .WithDescription("Find similar token variants (likely misspellings) in the vault.");
    config.AddCommand<RebuildCommand>("rebuild")
        .WithDescription("Force a full rebuild of the word list.");
});
return app.Run(args);
