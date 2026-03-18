using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Profiler.Web.Connectors;

public class YouTubeConnector : IConnector
{
    private readonly HttpClient _http;
    private readonly string _accessToken;
    public string Name => "YouTube";

    public YouTubeConnector(HttpClient http, string accessToken)
    {
        _http = http;
        _accessToken = accessToken;
    }

    private static string Slugify(string input)
    {
        var slug = input.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = slug.Trim('-');
        return slug;
    }

    private async Task<JsonDocument?> TryGetJson(string url)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            req.Headers.Add("User-Agent", "Profiler.Web/1.0");
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            return JsonDocument.Parse(json);
        }
        catch { return null; }
    }

    public async Task<ProfileData> FetchAsync()
    {
        var features = new List<string>();

        using var subs = await TryGetJson(
            "https://www.googleapis.com/youtube/v3/subscriptions?part=snippet&mine=true&maxResults=50");
        if (subs != null && subs.RootElement.TryGetProperty("items", out var subsItems))
        {
            var channelCount = 0;
            foreach (var sub in subsItems.EnumerateArray())
            {
                if (!sub.TryGetProperty("snippet", out var snippet)) continue;

                if (channelCount < 30 && snippet.TryGetProperty("title", out var titleProp))
                {
                    var title = titleProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(title))
                    {
                        features.Add($"youtube-channel:{Slugify(title)}");
                        channelCount++;
                    }
                }

                if (snippet.TryGetProperty("description", out var descProp))
                {
                    var desc = descProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(desc))
                    {
                        var words = desc.Split(new[] { ' ', '\t', '\n', '\r' },
                                               StringSplitOptions.RemoveEmptyEntries)
                                        .Take(3);
                        foreach (var word in words)
                        {
                            var slug = Slugify(word);
                            if (!string.IsNullOrEmpty(slug))
                                features.Add($"youtube-topic:{slug}");
                        }
                    }
                }
            }
        }

        var uniqueTags = new HashSet<string>();
        using var liked = await TryGetJson(
            "https://www.googleapis.com/youtube/v3/videos?part=snippet&myRating=like&maxResults=50");
        if (liked != null && liked.RootElement.TryGetProperty("items", out var likedItems))
        {
            foreach (var video in likedItems.EnumerateArray())
            {
                if (!video.TryGetProperty("snippet", out var snippet)) continue;

                if (snippet.TryGetProperty("tags", out var tagsProp))
                {
                    var count = 0;
                    foreach (var tag in tagsProp.EnumerateArray())
                    {
                        if (count >= 5) break;
                        var tagStr = tag.GetString() ?? "";
                        if (!string.IsNullOrEmpty(tagStr))
                        {
                            var slug = Slugify(tagStr);
                            if (uniqueTags.Count < 30 && uniqueTags.Add(slug))
                                features.Add($"youtube-tag:{slug}");
                            count++;
                        }
                    }
                }

                if (snippet.TryGetProperty("categoryId", out var catProp))
                {
                    var catId = catProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(catId))
                        features.Add($"youtube-category:{catId}");
                }
            }
        }

        return new ProfileData("YouTube", features.Distinct().ToList());
    }
}
