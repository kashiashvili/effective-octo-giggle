using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Profiler.Web.Connectors;

public class SoundCloudConnector : IConnector
{
    private readonly HttpClient _http;
    private readonly string _accessToken;
    public string Name => "SoundCloud";

    public SoundCloudConnector(HttpClient http, string accessToken)
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

    private const int HashPrefixLength = 12;

    private static string Sha256Prefix(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..HashPrefixLength].ToLowerInvariant();
    }

    private async Task<JsonDocument?> TryGetJson(string url)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("OAuth", _accessToken);
            req.Headers.Add("User-Agent", "Profiler.Web/1.0");
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            return JsonDocument.Parse(json);
        }
        catch { return null; }
    }

    private static IEnumerable<string> ParseTagList(string tagList)
    {
        // SoundCloud tag_list: space-separated, quoted tags can contain spaces
        var tags = new List<string>();
        foreach (Match m in Regex.Matches(tagList, @"""([^""]+)""|(\S+)"))
        {
            var tag = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            if (!string.IsNullOrWhiteSpace(tag))
                tags.Add(tag);
        }
        return tags;
    }

    public async Task<ProfileData> FetchAsync()
    {
        var features = new List<string>();

        // Verify auth
        using var me = await TryGetJson("https://api.soundcloud.com/me");
        if (me == null) return new ProfileData("SoundCloud", features);

        using var likes = await TryGetJson("https://api.soundcloud.com/me/likes/tracks?limit=100");
        if (likes != null)
        {
            foreach (var track in likes.RootElement.EnumerateArray())
            {
                if (track.TryGetProperty("genre", out var genreProp))
                {
                    var genre = genreProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(genre))
                        features.Add($"soundcloud-genre:{Slugify(genre)}");
                }

                if (track.TryGetProperty("tag_list", out var tagListProp))
                {
                    var tagList = tagListProp.GetString() ?? "";
                    var count = 0;
                    foreach (var tag in ParseTagList(tagList))
                    {
                        if (count >= 5) break;
                        features.Add($"soundcloud-tag:{Slugify(tag)}");
                        count++;
                    }
                }

                if (track.TryGetProperty("title", out var titleProp))
                {
                    var title = titleProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(title))
                        features.Add($"soundcloud-track:{Sha256Prefix(title)}");
                }
            }
        }

        using var followings = await TryGetJson("https://api.soundcloud.com/me/followings?limit=100");
        if (followings != null)
        {
            var count = 0;
            foreach (var user in followings.RootElement.EnumerateArray())
            {
                if (count >= 20) break;
                if (user.TryGetProperty("username", out var usernameProp))
                {
                    var username = usernameProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(username))
                    {
                        features.Add($"soundcloud-follows:{Slugify(username)}");
                        count++;
                    }
                }
            }
        }

        return new ProfileData("SoundCloud", features.Distinct().ToList());
    }
}
