using System.Text.Json;
using System.Text.RegularExpressions;

namespace Profiler.Web.Connectors;

public class SteamConnector : IConnector
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _steamId;
    public string Name => "Steam";

    public SteamConnector(HttpClient http, string apiKey, string steamId)
    {
        _http = http;
        _apiKey = apiKey;
        _steamId = steamId;
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
        var features = new HashSet<string>();

        using var ownedGames = await TryGetJson(
            $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={_apiKey}&steamid={_steamId}&include_appinfo=true&format=json");
        if (ownedGames != null &&
            ownedGames.RootElement.TryGetProperty("response", out var response) &&
            response.TryGetProperty("games", out var games))
        {
            long totalMinutes = 0;
            int gameCount = 0;
            var gameList = games.EnumerateArray().ToList();

            foreach (var game in gameList)
            {
                long playtimeMinutes = 0;
                if (game.TryGetProperty("playtime_forever", out var pt))
                    playtimeMinutes = pt.GetInt64();
                totalMinutes += playtimeMinutes;

                if (playtimeMinutes > 60 && gameCount < 50 && game.TryGetProperty("name", out var nameProp))
                {
                    var name = nameProp.GetString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        features.Add($"steam-game:{Slugify(name)}");
                        gameCount++;
                    }
                }
            }

            var totalHours = totalMinutes / 60.0;
            var hoursBucket = totalHours < 10 ? "casual" : totalHours <= 100 ? "regular" : "heavy";
            features.Add($"steam-hours:{hoursBucket}");
        }

        using var friends = await TryGetJson(
            $"https://api.steampowered.com/ISteamUser/GetFriendList/v0001/?key={_apiKey}&steamid={_steamId}&relationship=friend");
        if (friends != null &&
            friends.RootElement.TryGetProperty("friendslist", out var friendsList) &&
            friendsList.TryGetProperty("friends", out var friendsArr))
        {
            var count = friendsArr.EnumerateArray().Count();
            var bucket = count < 10 ? "solo" : count <= 50 ? "social" : "popular";
            features.Add($"steam-friend-count:{bucket}");
        }

        return new ProfileData("Steam", features.ToList());
    }
}
