using Profiler.Web.Profile;

namespace Profiler.Web.Matching;

public class UserMatcher
{
    private readonly Dictionary<string, (ProfileFingerprint fp, string username)> _store = new();

    public void Add(string userId, ProfileFingerprint fp, string username = "")
    {
        _store[userId] = (fp, username);
    }

    public List<MatchResult> FindMatches(string queryUserId, int topK = 20)
    {
        if (!_store.TryGetValue(queryUserId, out var queryEntry))
            return new List<MatchResult>();

        var results = new List<MatchResult>();
        foreach (var (uid, (fp, username)) in _store)
        {
            if (uid == queryUserId) continue;
            var sim = queryEntry.fp.Similarity(fp);
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
