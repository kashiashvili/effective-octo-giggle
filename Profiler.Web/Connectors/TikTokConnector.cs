using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Profiler.Web.Connectors;

public class TikTokConnector : IConnector
{
    private readonly HttpClient _http;
    private readonly string _accessToken;
    public string Name => "TikTok";

    public TikTokConnector(HttpClient http, string accessToken)
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

        using var videos = await TryGetJson(
            "https://open.tiktokapis.com/v2/video/list/?fields=title,video_description,hashtag_names&max_count=20");
        if (videos != null
            && videos.RootElement.TryGetProperty("data", out var videoData)
            && videoData.TryGetProperty("videos", out var videoList))
        {
            foreach (var video in videoList.EnumerateArray())
            {
                if (video.TryGetProperty("hashtag_names", out var hashtagNames))
                {
                    foreach (var tag in hashtagNames.EnumerateArray())
                    {
                        var t = tag.GetString() ?? "";
                        if (!string.IsNullOrEmpty(t))
                            features.Add($"tiktok-hashtag:{Slugify(t)}");
                    }
                }

                if (video.TryGetProperty("video_description", out var descProp))
                {
                    var desc = descProp.GetString() ?? "";
                    foreach (Match m in Regex.Matches(desc, @"#(\w+)"))
                        features.Add($"tiktok-hashtag:{Slugify(m.Groups[1].Value)}");
                }
            }
        }

        using var following = await TryGetJson(
            "https://open.tiktokapis.com/v2/following/list/?max_count=100&fields=display_name");
        if (following != null
            && following.RootElement.TryGetProperty("data", out var followData)
            && followData.TryGetProperty("user_following", out var followList))
        {
            var count = 0;
            foreach (var user in followList.EnumerateArray())
            {
                if (count >= 30) break;
                if (user.TryGetProperty("display_name", out var nameProp))
                {
                    var name = nameProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(name))
                    {
                        features.Add($"tiktok-follows:{Slugify(name)}");
                        count++;
                    }
                }
            }
        }

        return new ProfileData("TikTok", features.Distinct().ToList());
    }
}
