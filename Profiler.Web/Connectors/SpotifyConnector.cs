using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Profiler.Web.Connectors;

public class SpotifyConnector : IConnector
{
    private readonly HttpClient _http;
    private readonly string _accessToken;
    public string Name => "Spotify";

    public SpotifyConnector(HttpClient http, string accessToken)
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

    private async Task<JsonDocument?> TryGetJson(string url, CancellationToken cancellationToken)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            using var resp = await _http.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            return JsonDocument.Parse(json);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    public async Task<ProfileData> FetchAsync(CancellationToken cancellationToken = default)
    {
        var features = new HashSet<string>();

        using var artists = await TryGetJson("https://api.spotify.com/v1/me/top/artists?limit=50&time_range=medium_term", cancellationToken);
        if (artists != null && artists.RootElement.TryGetProperty("items", out var artistItems))
        {
            int artistCount = 0;
            foreach (var artist in artistItems.EnumerateArray())
            {
                if (artist.TryGetProperty("genres", out var genres))
                {
                    foreach (var genre in genres.EnumerateArray())
                    {
                        var g = genre.GetString();
                        if (!string.IsNullOrEmpty(g))
                            features.Add($"spotify-genre:{Slugify(g)}");
                    }
                }

                if (artistCount < 20 && artist.TryGetProperty("name", out var artistName))
                {
                    var name = artistName.GetString();
                    if (!string.IsNullOrEmpty(name))
                        features.Add($"spotify-artist:{Slugify(name)}");
                    artistCount++;
                }
            }
        }

        using var tracks = await TryGetJson("https://api.spotify.com/v1/me/top/tracks?limit=50&time_range=medium_term", cancellationToken);
        if (tracks != null && tracks.RootElement.TryGetProperty("items", out var trackItems))
        {
            foreach (var track in trackItems.EnumerateArray())
            {
                if (track.TryGetProperty("album", out var album) &&
                    album.TryGetProperty("genres", out var albumGenres))
                {
                    foreach (var genre in albumGenres.EnumerateArray())
                    {
                        var g = genre.GetString();
                        if (!string.IsNullOrEmpty(g))
                            features.Add($"spotify-genre:{Slugify(g)}");
                    }
                }
            }
        }

        using var playlists = await TryGetJson("https://api.spotify.com/v1/me/playlists?limit=50", cancellationToken);
        if (playlists != null && playlists.RootElement.TryGetProperty("items", out var playlistItems))
        {
            foreach (var playlist in playlistItems.EnumerateArray())
            {
                var isPublic = false;
                if (playlist.TryGetProperty("public", out var pub) && pub.ValueKind == JsonValueKind.True)
                    isPublic = true;

                var trackCount = 0;
                if (playlist.TryGetProperty("tracks", out var tracksObj) &&
                    tracksObj.TryGetProperty("total", out var total))
                    trackCount = total.GetInt32();

                if (isPublic && trackCount > 5 && playlist.TryGetProperty("name", out var pName))
                {
                    var name = pName.GetString();
                    if (!string.IsNullOrEmpty(name))
                        features.Add($"spotify-playlist-topic:{Slugify(name)}");
                }
            }
        }

        return new ProfileData("Spotify", features.ToList());
    }
}
