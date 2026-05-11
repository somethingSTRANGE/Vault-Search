# Vault Search

A fuzzy keyword search CLI for folders of markdown files. Designed for use with AI assistants (such as Claude Code) that need to search a vault from the terminal, but equally useful as a standalone tool.

## Features

- **Fuzzy search** — finds results even when query terms are misspelled or vary slightly
- **Alias expansion** — map a search token to a set of equivalent terms (synonyms, proper noun variants)
- **Scope filtering** — restrict indexing and search to specific folders
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
```

Searches the vault for files matching the query. Each term is fuzzy-expanded against the word list before searching, so near-matches are included automatically.

| Option | Default | Description |
|---|---|---|
| `--top N` | `10` | Maximum number of results |
| `--vault PATH` | current directory | Path to the vault folder |
| `--data PATH` | auto-detected | Path to the data directory (DB and config) |
| `--rebuild` | — | Force a word list rebuild before searching |

### Spellings

```
vault-search spellings [token] [--vault PATH] [--data PATH] [--threshold N]
```

Without a token, audits the entire word list for likely misspellings — tokens that appear rarely and are similar to a much more frequent token. With a token, shows all similar variants of that specific word.

| Option | Default | Description |
|---|---|---|
| `--threshold N` | `20` | Minimum frequency for a token to be treated as canonical |
| `--vault PATH` | current directory | Path to the vault folder |
| `--data PATH` | auto-detected | Path to the data directory |

### Rebuild

```
vault-search rebuild [--vault PATH] [--data PATH]
```

Forces a full rebuild of the word list index regardless of whether files appear to have changed.

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

On first run, vault-search writes commented stub files to the `config/` directory. Edit them to customize behaviour.

### `aliases.yaml` — semantic query expansion

Maps a search token to a list of equivalent terms. All aliases are searched whenever the key appears in a query. Use this for synonyms, alternate spellings, or multi-word phrases that fuzzy matching cannot handle.

```yaml
nightfall:
  - dusk
  - evening storm
```

### `stopwords-extra.txt` — additional stopwords

One word per line. Words listed here are excluded from the word list during rebuild. The built-in list already covers common English words; use this file for domain-specific terms you want to suppress.

```
lorem
ipsum
```

### `scope.txt` — folder include/exclude rules

Controls which folders are indexed and searched. Rules are applied top-to-bottom; the last matching rule wins. With no active rules, all folders are included.

```
# Exclude everything, then include specific folders:
-*
+Projects
+Areas
```

Patterns match folder names at any depth prefix. `*` matches everything.

## How it works

1. **Dirty check** — scans file modification times; skips rebuild if nothing has changed
2. **Rebuild** — tokenizes all `.md` file contents and filenames into a SQLite word list, stripping stopwords
3. **Query expansion** — each query token is expanded via the alias dictionary and fuzzy-matched against the word list; all candidates are searched
4. **Search** — ripgrep searches the vault (or scoped folders) for the expanded term set
5. **Rank** — results are scored by number of matching terms and returned as a ranked table
