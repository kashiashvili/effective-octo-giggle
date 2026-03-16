using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Profiler.Web.Connectors;

public class LastFmConnector : IConnector
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _username;
    public string Name => "Last.fm";

    public LastFmConnector(HttpClient http, string apiKey, string username)
    {
        _http = http;
        _apiKey = apiKey;
        _username = username;
    }

    private static string Slugify(string input)
    {
        var slug = input.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = slug.Trim('-');
        return slug;
    }

    private static string HashTrack(string artist, string track)
    {
        var input = $"{artist} - {track}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
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
        var features = new HashSet<string>();
        var baseUrl = "https://ws.audioscrobbler.com/2.0/";

        using var topArtists = await TryGetJson(
            $"{baseUrl}?method=user.getTopArtists&user={Uri.EscapeDataString(_username)}&api_key={_apiKey}&format=json&limit=100");
        if (topArtists != null &&
            topArtists.RootElement.TryGetProperty("topartists", out var topartists) &&
            topartists.TryGetProperty("artist", out var artistArr))
        {
            int count = 0;
            foreach (var artist in artistArr.EnumerateArray())
            {
                if (count >= 30) break;
                if (artist.TryGetProperty("name", out var nameProp))
                {
                    var name = nameProp.GetString();
                    if (!string.IsNullOrEmpty(name))
                        features.Add($"lastfm-artist:{Slugify(name)}");
                    count++;
                }
            }
        }

        using var topTags = await TryGetJson(
            $"{baseUrl}?method=user.getTopTags&user={Uri.EscapeDataString(_username)}&api_key={_apiKey}&format=json&limit=50");
        if (topTags != null &&
            topTags.RootElement.TryGetProperty("toptags", out var toptags) &&
            toptags.TryGetProperty("tag", out var tagArr))
        {
            foreach (var tag in tagArr.EnumerateArray())
            {
                if (tag.TryGetProperty("name", out var nameProp))
                {
                    var name = nameProp.GetString();
                    if (!string.IsNullOrEmpty(name))
                        features.Add($"lastfm-tag:{Slugify(name)}");
                }
            }
        }

        using var topTracks = await TryGetJson(
            $"{baseUrl}?method=user.getTopTracks&user={Uri.EscapeDataString(_username)}&api_key={_apiKey}&format=json&limit=100");
        if (topTracks != null &&
            topTracks.RootElement.TryGetProperty("toptracks", out var toptracks) &&
            toptracks.TryGetProperty("track", out var trackArr))
        {
            foreach (var track in trackArr.EnumerateArray())
            {
                var trackName = track.TryGetProperty("name", out var tName) ? tName.GetString() ?? "" : "";
                var artistName = (track.TryGetProperty("artist", out var artist) &&
                    artist.TryGetProperty("name", out var aName)) ? aName.GetString() ?? "" : "";

                if (!string.IsNullOrEmpty(trackName))
                    features.Add($"lastfm-track:{HashTrack(artistName, trackName)}");
            }
        }

        return new ProfileData("Last.fm", features.ToList());
    }
}
