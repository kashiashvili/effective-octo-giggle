using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Profiler.Web.Connectors;

public class TwitchConnector : IConnector
{
    private readonly HttpClient _http;
    private readonly string _accessToken;
    private readonly string _clientId;
    public string Name => "Twitch";

    public TwitchConnector(HttpClient http, string accessToken, string clientId)
    {
        _http = http;
        _accessToken = accessToken;
        _clientId = clientId;
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
            req.Headers.Add("Client-Id", _clientId);
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

        using var userDoc = await TryGetJson("https://api.twitch.tv/helix/users");
        string userId = "";
        if (userDoc != null
            && userDoc.RootElement.TryGetProperty("data", out var userData)
            && userData.GetArrayLength() > 0)
        {
            var user = userData[0];
            if (user.TryGetProperty("id", out var idProp))
                userId = idProp.GetString() ?? "";
        }

        if (string.IsNullOrEmpty(userId))
            return new ProfileData("Twitch", features);

        using var streams = await TryGetJson(
            $"https://api.twitch.tv/helix/streams/followed?user_id={userId}&first=100");
        if (streams != null && streams.RootElement.TryGetProperty("data", out var streamsData))
        {
            foreach (var stream in streamsData.EnumerateArray())
            {
                if (stream.TryGetProperty("game_name", out var gameNameProp))
                {
                    var gameName = gameNameProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(gameName))
                        features.Add($"twitch-game:{Slugify(gameName)}");
                }
                if (stream.TryGetProperty("game_id", out var gameIdProp))
                {
                    var gameId = gameIdProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(gameId))
                        features.Add($"twitch-category:{gameId}");
                }
            }
        }

        using var channels = await TryGetJson(
            $"https://api.twitch.tv/helix/channels/followed?user_id={userId}&first=100");
        if (channels != null && channels.RootElement.TryGetProperty("data", out var channelsData))
        {
            var count = 0;
            foreach (var channel in channelsData.EnumerateArray())
            {
                if (count >= 30) break;
                if (channel.TryGetProperty("broadcaster_name", out var nameProp))
                {
                    var name = nameProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(name))
                    {
                        features.Add($"twitch-channel:{Slugify(name)}");
                        count++;
                    }
                }
            }
        }

        return new ProfileData("Twitch", features.Distinct().ToList());
    }
}
