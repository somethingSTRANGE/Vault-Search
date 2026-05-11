# Vault Search

A fuzzy keyword search CLI for folders of markdown files. Designed for use with AI assistants (such as Claude Code) that need to search a vault from the terminal, but equally useful as a standalone tool.

## Features

- **Fuzzy search** — finds results even when query terms are misspelled or vary slightly
- **Alias expansion** — map a search token to a set of equivalent terms (synonyms, proper noun variants)
- **Frontmatter alias extraction** — automatically reads Obsidian `aliases:` frontmatter and wires them into query expansion at rebuild time
- **Scope filtering** — restrict indexing and search to specific folders, with named presets for common searches
- **Frontmatter filtering** — filter results by `type` value or property presence
- **Misspelling audit** — surface likely spelling variants across your vault
- **Fast dirty check** — rebuilds the word list only when files have changed

## Requirements

- [ripgrep](https://github.com/BurntSushi/ripgrep) (`rg`) on your PATH
- Windows x64 (for the pre-built binary); other platforms require building from source

## Installation

Download the latest release binary (`vault-search.exe`) and place it somewhere on your PATH.

## Usage

Run from the vault folder, or pass `--vault` to point at it explicitly.

### Search

```
vault-search "query terms" [--top N] [--vault PATH] [--data PATH] [--rebuild]
                           [--scope NAME] [--type VALUE] [--property NAME] [--pretty]
```

Searches the vault for files matching the query. Each term is fuzzy-expanded against the word list before searching, so near-matches are included automatically. Frontmatter `aliases:` properties are extracted at rebuild time and merged into query expansion.

| Option | Default | Description |
|---|---|---|
| `--top N` | `10` | Maximum number of results |
| `--vault PATH` | current directory | Path to the vault folder |
| `--data PATH` | auto-detected | Path to the data directory (DB and config) |
| `--rebuild` | — | Force a word list rebuild before searching |
| `--scope NAME` | — | Named search scope preset (loads `scope-<name>.txt`) |
| `--type VALUE` | — | Filter results to files where frontmatter `type` matches this value |
| `--property NAME` | — | Filter results to files that have this frontmatter property set (any value) |
| `--pretty` | — | Render results as a formatted table instead of plain structured text |

`--type` and `--property` are applied before the top N cutoff, so you always get up to N results from the matching set.

By default, output is plain structured text (one result per block) suited for machine parsing. Use `--pretty` for a formatted table when running interactively.

### Spellings

```
vault-search spellings [token] [--vault PATH] [--data PATH] [--threshold N]
                               [--scope NAME] [--type VALUE] [--property NAME] [--pretty]
```

Without a token, audits the entire word list for likely misspellings — tokens that appear rarely and are similar to a much more frequent token. With a token, shows all similar variants of that specific word.

| Option | Default | Description |
|---|---|---|
| `--threshold N` | `20` | Minimum frequency for a token to be treated as canonical |
| `--vault PATH` | current directory | Path to the vault folder |
| `--data PATH` | auto-detected | Path to the data directory |
| `--scope NAME` | — | Named search scope preset |
| `--type VALUE` | — | Filter file results by frontmatter `type` value |
| `--property NAME` | — | Filter file results to files with this frontmatter property |
| `--pretty` | — | Render results as a formatted table instead of plain structured text |

### Rebuild

```
vault-search rebuild [--vault PATH] [--data PATH]
```

Forces a full rebuild of the word list index and re-extracts frontmatter aliases, regardless of whether files appear to have changed.

### Scopes

```
vault-search scopes [--vault PATH] [--data PATH]
```

Lists available scope presets. Shows the default `scope.txt` and any named presets (`scope-<name>.txt`) with the exact flag needed to use each one.

## Data directory

vault-search stores its database and config files in a data directory alongside your vault. The location is auto-detected:

1. `--data PATH` if provided
2. `.obsidian/tools/vault-search/` if the vault contains an `.obsidian` folder (Obsidian vault)
3. `.vault-search/` at the vault root otherwise

The data directory contains two subdirectories:

- `config/` — user-edited configuration files (see below)
- `db/` — the SQLite word list index (rebuild-able; safe to delete)

**Obsidian Sync users:** exclude the `db/` folder from sync — it is large and regenerated automatically. The `config/` folder should sync normally.

## Configuration

On first run, vault-search writes commented stub files to the `config/` directory. Edit them to customise behaviour.

### `aliases.yaml` — semantic query expansion

Maps a search token to a list of equivalent terms. The key token is always searched; aliases are added on top. Use this for synonyms, alternate spellings, or multi-word phrases that fuzzy matching cannot handle.

```yaml
nightfall:
  - dusk
  - evening storm
```

### `fm-aliases.yaml` — auto-generated frontmatter aliases

Generated automatically during every rebuild from the `aliases:` frontmatter property of your markdown files. **Do not edit** — changes will be overwritten. Each token from a file's name is mapped to the tokens extracted from its frontmatter aliases list.

### `stopwords-extra.txt` — stopword customisation

One word per line. Lines starting with `#` are ignored. Plain words are added to the built-in stopword list. Prefix a word with `-` to remove it from the built-in list.

```
# Add a custom stopword:
lorem

# Remove a built-in stopword (e.g. to make a character name searchable):
-les
```

### `scope.txt` — folder include/exclude rules

Controls which folders are indexed and searched. Rules are applied top-to-bottom; the last matching rule wins. With no active rules, all folders are included.

```
# Exclude everything, then include specific folders:
-*
+Projects
+Areas

# Sub-exclude a noisy subfolder:
-Areas/Archive
```

#### Named scope presets

Create additional scope files as `scope-<name>.txt` to define reusable search presets. Select a preset at search time with `--scope <name>`. The default `scope.txt` is always used for rebuilding; named scopes only affect which folders ripgrep searches.

```
# config/scope-characters.txt
-*
+Areas/Characters
```

```
vault-search "father" --scope characters --type profile
```

Use `vault-search scopes` to list all available presets.

## How it works

1. **Dirty check** — scans file modification times; skips rebuild if nothing has changed
2. **Rebuild** — tokenizes all `.md` file contents and filenames into a SQLite word list, stripping stopwords; extracts frontmatter `aliases:` into `fm-aliases.yaml`
3. **Query expansion** — each query token is expanded via the alias dictionaries (user and frontmatter) and fuzzy-matched against the word list; all candidates are searched
4. **Scope** — if a named `--scope` is given, only the folders defined in that preset are searched; otherwise the default `scope.txt` applies
5. **Search** — ripgrep searches the scoped folders for the expanded term set
6. **Filter** — `--type` and `--property` filter results by frontmatter before the top N cutoff
7. **Rank** — results are scored by number of matching terms and returned as a ranked table
