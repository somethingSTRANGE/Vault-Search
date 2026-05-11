namespace VaultSearch.Config;

public static class Stopwords
{
    private static readonly HashSet<string> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "about", "above", "after", "again", "against", "all", "also", "am", "an",
        "and", "any", "are", "aren't", "as", "at", "be", "because", "been", "before",
        "being", "below", "between", "both", "but", "by", "can", "can't", "cannot",
        "could", "couldn't", "did", "didn't", "do", "does", "doesn't", "doing", "don't",
        "down", "during", "each", "few", "for", "from", "further", "get", "got", "had",
        "hadn't", "has", "hasn't", "have", "haven't", "having", "he", "he'd", "he'll",
        "he's", "her", "here", "here's", "hers", "herself", "him", "himself", "his",
        "how", "how's", "i", "i'd", "i'll", "i'm", "i've", "if", "in", "into", "is",
        "isn't", "it", "it's", "its", "itself", "just", "let's", "like", "make", "me",
        "more", "most", "mustn't", "my", "myself", "no", "nor", "not", "now", "of",
        "off", "on", "once", "only", "or", "other", "ought", "our", "ours", "ourselves",
        "out", "over", "own", "same", "say", "see", "shan't", "she", "she'd", "she'll",
        "she's", "should", "shouldn't", "so", "some", "such", "than", "that", "that's",
        "the", "their", "theirs", "them", "themselves", "then", "there", "there's",
        "these", "they", "they'd", "they'll", "they're", "they've", "this", "those",
        "through", "to", "too", "under", "until", "up", "us", "very", "was", "wasn't",
        "we", "we'd", "we'll", "we're", "we've", "were", "weren't", "what", "what's",
        "when", "when's", "where", "where's", "which", "while", "who", "who's", "whom",
        "why", "why's", "will", "with", "won't", "would", "wouldn't", "you", "you'd",
        "you'll", "you're", "you've", "your", "yours", "yourself", "yourselves",
        "said", "say", "look", "looked", "go", "going", "went", "gone", "come", "came",
        "know", "knew", "think", "thought", "want", "wanted", "need", "needed", "put",
        "take", "took", "taken", "see", "saw", "seen", "get", "got", "give", "gave",
        "given", "use", "used", "find", "found", "tell", "told", "ask", "asked",
        "seem", "seemed", "call", "called", "try", "tried", "keep", "kept", "let",
        "begin", "began", "begun", "show", "showed", "shown", "hear", "heard",
        "play", "run", "ran", "move", "live", "feel", "felt", "set", "turn", "turned"
    };

    public static IReadOnlySet<string> GetAll(string? extraStopwordsPath = null)
    {
        if (extraStopwordsPath is null || !File.Exists(extraStopwordsPath))
            return BuiltIn;

        var combined = new HashSet<string>(BuiltIn, StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(extraStopwordsPath))
        {
            var word = line.Trim();
            if (word.Length == 0 || word.StartsWith('#')) continue;
            if (word.StartsWith('-'))
                combined.Remove(word[1..].Trim());
            else
                combined.Add(word);
        }
        return combined;
    }
}
