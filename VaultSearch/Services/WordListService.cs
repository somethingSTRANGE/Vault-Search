using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace VaultSearch.Services;

public class WordListService
{
    private static readonly Regex TokenPattern = new(
        @"[A-Za-z0-9](?:[A-Za-z0-9\-']*[A-Za-z0-9])?",
        RegexOptions.Compiled);

    private readonly string _dbPath;
    private readonly string _vaultPath;

    public WordListService(string vaultPath, string dbPath)
    {
        _vaultPath = vaultPath;
        _dbPath = dbPath;
    }

    public bool IsDirty()
    {
        var lastBuilt = GetLastBuilt();
        if (lastBuilt is null) return true;

        return Directory.EnumerateFiles(_vaultPath, "*.md", SearchOption.AllDirectories)
            .Any(f => File.GetLastWriteTimeUtc(f) > lastBuilt.Value);
    }

    public void Rebuild(IReadOnlySet<string> stopwords, ScopeFilter scope)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        using var conn = OpenConnection();
        InitSchema(conn);

        using var tx = conn.BeginTransaction();

        using (var deleteCmd = conn.CreateCommand())
        {
            deleteCmd.Transaction = tx;
            deleteCmd.CommandText = "DELETE FROM tokens";
            deleteCmd.ExecuteNonQuery();
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(_vaultPath, "*.md", SearchOption.AllDirectories)
                     .Where(f => scope.IsIncluded(_vaultPath, f)))
        {
            IndexFilename(file, counts, stopwords);
            IndexContent(file, counts, stopwords);
        }

        using (var insertCmd = conn.CreateCommand())
        {
            insertCmd.Transaction = tx;
            insertCmd.CommandText = "INSERT OR REPLACE INTO tokens (token, frequency) VALUES (@t, @f)";
            var tp = insertCmd.Parameters.Add("@t", SqliteType.Text);
            var fp = insertCmd.Parameters.Add("@f", SqliteType.Integer);

            foreach (var (token, freq) in counts)
            {
                tp.Value = token;
                fp.Value = freq;
                insertCmd.ExecuteNonQuery();
            }
        }

        using (var metaCmd = conn.CreateCommand())
        {
            metaCmd.Transaction = tx;
            metaCmd.CommandText = "INSERT OR REPLACE INTO metadata (key, value) VALUES ('last_built', @v)";
            metaCmd.Parameters.AddWithValue("@v", DateTime.UtcNow.ToString("O"));
            metaCmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public IReadOnlyList<(string Token, int Frequency)> GetAllTokens()
    {
        if (!File.Exists(_dbPath)) return [];

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT token, frequency FROM tokens ORDER BY frequency DESC";
        using var reader = cmd.ExecuteReader();

        var results = new List<(string, int)>();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetInt32(1)));
        return results;
    }

    public DateTime? GetLastBuilt()
    {
        if (!File.Exists(_dbPath)) return null;

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM metadata WHERE key = 'last_built'";
        var val = cmd.ExecuteScalar() as string;
        return val is null
            ? null
            : DateTime.Parse(val, null, System.Globalization.DateTimeStyles.RoundtripKind);
    }

    private void IndexFilename(string filePath, Dictionary<string, int> counts, IReadOnlySet<string> stopwords)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        foreach (Match m in TokenPattern.Matches(name))
            AddToken(m.Value.ToLowerInvariant(), counts, stopwords);
    }

    private static void IndexContent(string filePath, Dictionary<string, int> counts, IReadOnlySet<string> stopwords)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            foreach (Match m in TokenPattern.Matches(content))
                AddToken(m.Value.ToLowerInvariant(), counts, stopwords);
        }
        catch (IOException) { }
    }

    private static void AddToken(string token, Dictionary<string, int> counts, IReadOnlySet<string> stopwords)
    {
        if (token.Length < 2 || stopwords.Contains(token)) return;
        counts[token] = counts.GetValueOrDefault(token) + 1;
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        conn.Open();
        return conn;
    }

    private static void InitSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS tokens (
                token TEXT NOT NULL PRIMARY KEY,
                frequency INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS metadata (
                key TEXT NOT NULL PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
