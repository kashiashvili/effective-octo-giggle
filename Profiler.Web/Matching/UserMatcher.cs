using Profiler.Web.Profile;

namespace Profiler.Web.Matching;

public class UserMatcher
{
    private readonly Dictionary<string, (ProfileFingerprint fp, string username)> _store = new();

    public void Add(string userId, ProfileFingerprint fp, string username = "")
    {
        _store[userId] = (fp, username);
    }

    /// <summary>Number of candidate fingerprints other than the query user, ignoring empty ones and any the caller excludes.</summary>
    public int CandidateCount(string queryUserId, Func<string, bool>? exclude = null) =>
        _store.Count(kv => kv.Key != queryUserId && !kv.Value.fp.IsEmpty && !(exclude?.Invoke(kv.Key) ?? false));

    /// <param name="minSimilarity">
    /// Matches below this estimated Jaccard similarity are dropped. With a MinHash estimate the
    /// sampling error is non-trivial, so very low scores are indistinguishable from having nothing
    /// in common; the caller supplies a floor below which a "match" would be misleading.
    /// </param>
    /// <param name="exclude">
    /// Candidates to leave out for this query — the people hidden between the query user and them.
    /// Kept per query rather than baked into the store so the same store can answer "who would
    /// they see?" for another person with their own exclusions.
    /// </param>
    public List<MatchResult> FindMatches(string queryUserId, int topK = 20, double minSimilarity = 0.0, Func<string, bool>? exclude = null)
    {
        if (!_store.TryGetValue(queryUserId, out var queryEntry))
            return new List<MatchResult>();

        // Empty signatures carry no information; comparing two of them would yield a bogus 100% match.
        if (queryEntry.fp.IsEmpty)
            return new List<MatchResult>();

        var results = new List<MatchResult>();
        foreach (var (uid, (fp, username)) in _store)
        {
            if (uid == queryUserId) continue;
            if (fp.IsEmpty) continue;
            if (exclude != null && exclude(uid)) continue;
            var sim = queryEntry.fp.Similarity(fp);
            if (sim < minSimilarity) continue;
            results.Add(new MatchResult
            {
                UserId = uid,
                Username = username,
                Similarity = sim
            });
        }

        // Deterministic on ties, so a cut at the top-K boundary never depends on insertion order.
        results.Sort((a, b) =>
        {
            var bySimilarity = b.Similarity.CompareTo(a.Similarity);
            return bySimilarity != 0 ? bySimilarity : string.CompareOrdinal(a.UserId, b.UserId);
        });
        return results.Take(topK).ToList();
    }

    /// <summary>
    /// How many candidates rank strictly closer to the query user than <paramref name="similarity"/>,
    /// stopping early at <paramref name="stopAt"/>. Answers "would someone with this score be in the
    /// query user's top list?" without building or sorting that list; ties count in the asker's favour.
    /// </summary>
    public int CountCloserThan(string queryUserId, double similarity, Func<string, bool>? exclude = null, int stopAt = int.MaxValue)
    {
        if (!_store.TryGetValue(queryUserId, out var queryEntry) || queryEntry.fp.IsEmpty)
            return 0;

        var closer = 0;
        foreach (var (uid, (fp, _)) in _store)
        {
            if (uid == queryUserId) continue;
            if (fp.IsEmpty) continue;
            if (exclude != null && exclude(uid)) continue;
            if (queryEntry.fp.Similarity(fp) > similarity && ++closer >= stopAt) break;
        }
        return closer;
    }
}
