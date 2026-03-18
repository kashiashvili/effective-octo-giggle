using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Profiler.Web.Connectors;

public class InstagramConnector : IConnector
{
    private readonly HttpClient _http;
    private readonly string _accessToken;
    public string Name => "Instagram";

    public InstagramConnector(HttpClient http, string accessToken)
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
            using var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            return JsonDocument.Parse(json);
        }
        catch { return null; }
    }

    public async Task<ProfileData> FetchAsync()
    {
        var features = new List<string>();

        var hashtagsSeen = new HashSet<string>();

        using var media = await TryGetJson(
            $"https://graph.instagram.com/me/media?fields=media_type,caption,hashtags&limit=50&access_token={_accessToken}");
        if (media != null && media.RootElement.TryGetProperty("data", out var mediaData))
        {
            foreach (var post in mediaData.EnumerateArray())
            {
                if (post.TryGetProperty("media_type", out var typeProp))
                {
                    var mediaType = (typeProp.GetString() ?? "").ToLowerInvariant();
                    if (!string.IsNullOrEmpty(mediaType))
                        features.Add($"instagram-media:{mediaType}");
                }

                if (post.TryGetProperty("caption", out var captionProp))
                {
                    var caption = captionProp.GetString() ?? "";
                    foreach (Match m in Regex.Matches(caption, @"#(\w+)"))
                    {
                        if (hashtagsSeen.Count >= 50) break;
                        var tag = Slugify(m.Groups[1].Value);
                        if (hashtagsSeen.Add(tag))
                            features.Add($"instagram-hashtag:{tag}");
                    }
                }
            }
        }

        using var tags = await TryGetJson(
            $"https://graph.instagram.com/me/tags?fields=id,name&access_token={_accessToken}");
        if (tags != null && tags.RootElement.TryGetProperty("data", out var tagsData))
        {
            foreach (var tag in tagsData.EnumerateArray())
            {
                if (tag.TryGetProperty("name", out var nameProp))
                {
                    var name = nameProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(name))
                        features.Add($"instagram-tag:{Slugify(name)}");
                }
            }
        }

        return new ProfileData("Instagram", features.Distinct().ToList());
    }
}
