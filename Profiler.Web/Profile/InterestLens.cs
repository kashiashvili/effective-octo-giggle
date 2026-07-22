namespace Profiler.Web.Profile;

/// <summary>One theme a person's interests fall under, with how many signals landed in it.</summary>
public record InterestTheme(string Name, int Count);

/// <summary>
/// Turns the raw features a connector produced into a themed, count-only summary shown to the person
/// once, at the moment their fingerprint is built. It exists to give a new user something the instant
/// they connect — before anyone has matched them — and to make the privacy promise concrete: this is
/// what we saw, and it is discarded here. Only theme names and counts leave this class; the raw
/// features never do, so nothing derived from it may be persisted.
/// </summary>
public static class InterestLens
{
    // Feature keys are "prefix:value". The prefix says which kind of signal it is; this maps each to
    // a human theme. Anything unrecognised is folded into "Other interests" rather than dropped, so
    // the totals still add up to something the person can trust.
    private static readonly (string Prefix, string Theme)[] Map =
    {
        ("language", "Programming & tech"),
        ("starred-topic", "Programming & tech"),
        ("repo-type", "Programming & tech"),
        ("org-type", "Programming & tech"),
        ("topic", "Programming & tech"),
        ("activity", "Programming & tech"),

        ("shelf", "Reading"),
        ("rating-high", "Reading"),
        ("genre", "Reading"),

        ("netflix-genre", "Film & TV"),
        ("netflix-type", "Film & TV"),
        ("netflix-watched", "Film & TV"),

        ("spotify-artist", "Music"),
        ("spotify-genre", "Music"),
        ("spotify-playlist-topic", "Music"),
        ("lastfm-artist", "Music"),
        ("lastfm-tag", "Music"),
        ("lastfm-track", "Music"),
        ("soundcloud-track", "Music"),
        ("soundcloud-genre", "Music"),
        ("soundcloud-tag", "Music"),
        ("soundcloud-follows", "Music"),

        ("steam-game", "Gaming"),
        ("steam-hours", "Gaming"),
        ("steam-friend-count", "Gaming"),
        ("twitch-game", "Gaming"),
        ("twitch-category", "Gaming"),
        ("twitch-channel", "Gaming"),

        ("youtube-topic", "Video"),
        ("youtube-tag", "Video"),
        ("youtube-channel", "Video"),
        ("youtube-category", "Video"),

        ("linkedin-skill", "Work & industry"),
        ("linkedin-title", "Work & industry"),
        ("linkedin-industry", "Work & industry"),

        ("reddit-sub", "Communities & topics"),
        ("twitter-topic", "Communities & topics"),
        ("twitter-hashtag", "Communities & topics"),
        ("instagram-hashtag", "Communities & topics"),
        ("instagram-tag", "Communities & topics"),
        ("instagram-media", "Communities & topics"),
        ("tiktok-hashtag", "Communities & topics"),
        ("tiktok-follows", "Communities & topics"),
        ("pinterest-topic", "Communities & topics"),
        ("pinterest-board", "Communities & topics"),
        ("pinterest-following-board", "Communities & topics"),
        ("fb-interest", "Communities & topics"),
        ("fb-like-category", "Communities & topics"),
        ("fb-edu-type", "Communities & topics"),
        ("interest", "Communities & topics"),
        ("rss-topic", "Communities & topics"),
        ("rss-keyword", "Communities & topics"),
        ("rss-feed", "Communities & topics"),
        ("locale", "Communities & topics"),
    };

    private const string Fallback = "Other interests";

    private static string ThemeFor(string feature)
    {
        var prefix = feature.Split(':', 2)[0];
        foreach (var (p, theme) in Map)
            if (p == prefix) return theme;
        return Fallback;
    }

    /// <summary>
    /// Themes present in the features, largest first, ties broken alphabetically so the order is
    /// stable. Blank/whitespace features are ignored.
    /// </summary>
    public static List<InterestTheme> Summarize(IEnumerable<string> features)
    {
        var counts = new Dictionary<string, int>();
        foreach (var f in features)
        {
            if (string.IsNullOrWhiteSpace(f)) continue;
            var theme = ThemeFor(f);
            counts[theme] = counts.GetValueOrDefault(theme) + 1;
        }

        return counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new InterestTheme(kv.Key, kv.Value))
            .ToList();
    }
}
