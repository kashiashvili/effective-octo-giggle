using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Profiler.Web.Connectors;

public class RssFeedsConnector : IConnector
{
    private readonly HttpClient _http;
    private readonly string _feedUrls;
    public string Name => "RSS/Blogs";

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "after", "again", "all", "also", "been", "before", "being", "both", "came",
        "come", "does", "done", "each", "from", "have", "here", "into", "just", "know", "like",
        "made", "make", "many", "more", "most", "much", "only", "over", "same", "since", "some",
        "such", "than", "that", "their", "them", "then", "there", "these", "they", "this",
        "those", "through", "time", "very", "want", "well", "were", "what", "when", "where",
        "which", "while", "will", "with", "your"
    };

    public RssFeedsConnector(HttpClient http, string feedUrls)
    {
        _http = http;
        _feedUrls = feedUrls;
    }

    private static string Slugify(string input)
    {
        var slug = input.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = slug.Trim('-');
        return slug;
    }

    private static IEnumerable<string> ParseUrls(string feedUrls)
    {
        return feedUrls
            .Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(u => u.Trim())
            .Where(u => Uri.TryCreate(u, UriKind.Absolute, out var uri)
                        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .Take(10);
    }

    private static IEnumerable<string> ExtractKeywords(string title)
    {
        return Regex.Split(title, @"\W+")
            .Where(w => w.Length > 4 && !StopWords.Contains(w))
            .Take(5)
            .Select(w => w.ToLowerInvariant());
    }

    /// <summary>
    /// Fetches a feed, following redirects manually so that every hop is re-checked by
    /// <see cref="SsrfGuard"/>. Auto-redirect is disabled on this connector's HttpClient, because
    /// a public URL that 302s to an internal address would otherwise bypass the guard.
    /// </summary>
    private async Task<string?> FetchWithGuardedRedirectsAsync(Uri uri, int maxHops = 3)
    {
        for (var hop = 0; hop <= maxHops; hop++)
        {
            if (!await SsrfGuard.IsAllowedAsync(uri)) return null;

            using var resp = await _http.GetAsync(uri);

            if ((int)resp.StatusCode is >= 300 and < 400)
            {
                var location = resp.Headers.Location;
                if (location == null) return null;
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
                continue;
            }

            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync();
        }
        return null;
    }

    public async Task<ProfileData> FetchAsync()
    {
        var features = new List<string>();
        var urls = ParseUrls(_feedUrls).ToList();

        foreach (var url in urls)
        {
            try
            {
                var xml = await FetchWithGuardedRedirectsAsync(new Uri(url));
                if (xml == null) continue;
                XDocument doc;
                try { doc = XDocument.Parse(xml); }
                catch { continue; }

                XNamespace atom = "http://www.w3.org/2005/Atom";

                // Feed title
                var feedTitle = doc.Descendants("title").FirstOrDefault()?.Value
                             ?? doc.Descendants(atom + "title").FirstOrDefault()?.Value
                             ?? "";
                if (!string.IsNullOrWhiteSpace(feedTitle))
                    features.Add($"rss-feed:{Slugify(feedTitle)}");

                // Items (RSS) or entries (Atom)
                var items = doc.Descendants("item").Concat(doc.Descendants(atom + "entry")).ToList();

                var topicsThisFeed = new HashSet<string>();
                var keywordsThisFeed = new HashSet<string>();

                foreach (var item in items)
                {
                    // Categories / tags
                    var categories = item.Elements("category")
                        .Concat(item.Elements(atom + "category"))
                        .Concat(item.Elements("tags"))
                        .Select(e =>
                        {
                            var term = e.Attribute("term")?.Value?.Trim();
                            var val = e.Value?.Trim();
                            return !string.IsNullOrEmpty(term) ? term : val ?? "";
                        })
                        .Where(c => !string.IsNullOrWhiteSpace(c));

                    foreach (var cat in categories)
                    {
                        if (topicsThisFeed.Count >= 5) break;
                        var slug = Slugify(cat);
                        if (!string.IsNullOrEmpty(slug) && topicsThisFeed.Add(slug))
                            features.Add($"rss-topic:{slug}");
                    }

                    // Keywords from title
                    var itemTitle = item.Element("title")?.Value
                                 ?? item.Element(atom + "title")?.Value
                                 ?? "";
                    if (!string.IsNullOrWhiteSpace(itemTitle))
                    {
                        foreach (var kw in ExtractKeywords(itemTitle))
                        {
                            if (keywordsThisFeed.Count >= 20) break;
                            if (keywordsThisFeed.Add(kw))
                                features.Add($"rss-keyword:{kw}");
                        }
                    }
                }
            }
            catch { /* silently skip */ }
        }

        return new ProfileData("RSS/Blogs", features.Distinct().ToList());
    }
}
