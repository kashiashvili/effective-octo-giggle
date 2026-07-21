using Profiler.Web.Profile;

namespace Profiler.Web.Matching;

public class UserMatcher
{
    private readonly Dictionary<string, (ProfileFingerprint fp, string username)> _store = new();

    public void Add(string userId, ProfileFingerprint fp, string username = "")
    {
        _store[userId] = (fp, username);
    }

    /// <summary>Number of candidate fingerprints other than the query user, ignoring empty ones.</summary>
    public int CandidateCount(string queryUserId) =>
        _store.Count(kv => kv.Key != queryUserId && !kv.Value.fp.IsEmpty);

    /// <param name="minSimilarity">
    /// Matches below this estimated Jaccard similarity are dropped. With a MinHash estimate the
    /// sampling error is non-trivial, so very low scores are indistinguishable from having nothing
    /// in common; the caller supplies a floor below which a "match" would be misleading.
    /// </param>
    public List<MatchResult> FindMatches(string queryUserId, int topK = 20, double minSimilarity = 0.0)
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
            var sim = queryEntry.fp.Similarity(fp);
            if (sim < minSimilarity) continue;
            results.Add(new MatchResult
            {
                UserId = uid,
                Username = username,
                Similarity = sim
            });
        }

        results.Sort((a, b) => b.Similarity.CompareTo(a.Similarity));
        return results.Take(topK).ToList();
    }
}
