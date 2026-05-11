using FuzzySharp;

namespace VaultSearch.Services;

public class FuzzyMatchService(int threshold = 70)
{
    public IReadOnlyList<string> ExpandQuery(string token, IReadOnlyList<(string Token, int Frequency)> wordList)
    {
        token = token.ToLowerInvariant();
        return wordList
            .Where(w => Fuzz.Ratio(token, w.Token) >= threshold)
            .Select(w => w.Token)
            .ToList();
    }

    public IReadOnlyList<(string Token, int Score, int Frequency)> FindSimilar(
        string token,
        IReadOnlyList<(string Token, int Frequency)> wordList,
        bool excludeExact = false)
    {
        token = token.ToLowerInvariant();
        return wordList
            .Where(w => !(excludeExact && w.Token == token))
            .Select(w => (w.Token, Score: Fuzz.Ratio(token, w.Token), w.Frequency))
            .Where(r => r.Score >= threshold)
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    public IReadOnlyList<(string Canonical, int CanonicalFreq, string Similar, int SimilarFreq)> FindMisspellings(
        IReadOnlyList<(string Token, int Frequency)> wordList,
        int frequencyThreshold,
        double misspellingRatio)
    {
        var highFreq = wordList.Where(w => w.Frequency >= frequencyThreshold).ToList();
        var results = new List<(string, int, string, int)>();

        foreach (var (canonical, canonicalFreq) in highFreq)
        {
            foreach (var (similar, similarFreq) in wordList)
            {
                if (similar == canonical) continue;
                if (similarFreq > canonicalFreq * misspellingRatio) continue;
                if (Fuzz.Ratio(canonical, similar) >= threshold)
                    results.Add((canonical, canonicalFreq, similar, similarFreq));
            }
        }

        return results.OrderBy(r => r.Item1).ThenByDescending(r => r.Item4).ToList();
    }
}
