using System.Net.Http.Headers;
using System.Text.Json;

namespace Profiler.Web.Connectors;

public class RedditConnector : IConnector
{
    private readonly HttpClient _http;
    private readonly string _accessToken;
    public string Name => "Reddit";

    public RedditConnector(HttpClient http, string accessToken)
    {
        _http = http;
        _accessToken = accessToken;
    }

    private async Task<JsonDocument?> TryGetJson(string url, CancellationToken cancellationToken)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            req.Headers.Add("User-Agent", "Profiler.Web/1.0");
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

        using var subs = await TryGetJson("https://oauth.reddit.com/subreddits/mine/subscriber?limit=100", cancellationToken);
        if (subs != null &&
            subs.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("children", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                if (!child.TryGetProperty("data", out var sub)) continue;

                if (sub.TryGetProperty("display_name", out var displayName))
                {
                    var name = displayName.GetString();
                    if (!string.IsNullOrEmpty(name))
                        features.Add($"reddit-sub:{name.ToLowerInvariant()}");
                }
            }
        }

        return new ProfileData("Reddit", features.ToList());
    }
}
