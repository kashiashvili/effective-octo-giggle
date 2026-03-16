using System.Net.Http.Headers;
using System.Text.Json;

namespace Profiler.Web.Connectors;

public class FacebookConnector : IConnector
{
    private readonly HttpClient _http;
    private readonly string _token;
    public string Name => "Facebook";

    public FacebookConnector(HttpClient http, string token)
    {
        _http = http;
        _token = token;
    }

    private async Task<JsonDocument?> TryGetJson(string url)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Headers.Add("User-Agent", "Profiler.Web/1.0");
            using var resp = await _http.SendAsync(req);
            if ((int)resp.StatusCode == 403) return null;
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            return JsonDocument.Parse(json);
        }
        catch { return null; }
    }

    public async Task<ProfileData> FetchAsync()
    {
        var features = new List<string>();

        const string baseUrl = "https://graph.facebook.com/v18.0";

        using var likes = await TryGetJson($"{baseUrl}/me/likes?fields=category&access_token={_token}");
        if (likes != null && likes.RootElement.TryGetProperty("data", out var likesData))
            foreach (var item in likesData.EnumerateArray())
                if (item.TryGetProperty("category", out var cat))
                    features.Add($"fb-like-category:{cat.GetString()?.ToLowerInvariant()}");

        using var interests = await TryGetJson($"{baseUrl}/me/interests?fields=name&access_token={_token}");
        if (interests != null && interests.RootElement.TryGetProperty("data", out var intData))
            foreach (var item in intData.EnumerateArray())
                if (item.TryGetProperty("name", out var n))
                    features.Add($"fb-interest:{n.GetString()?.ToLowerInvariant()}");

        using var me = await TryGetJson($"{baseUrl}/me?fields=education&access_token={_token}");
        if (me != null && me.RootElement.TryGetProperty("education", out var edu))
            foreach (var item in edu.EnumerateArray())
                if (item.TryGetProperty("type", out var t))
                    features.Add($"fb-edu-type:{t.GetString()?.ToLowerInvariant()}");

        return new ProfileData("Facebook", features.Distinct().ToList());
    }
}
