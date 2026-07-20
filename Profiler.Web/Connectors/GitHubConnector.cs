using System.Net.Http.Headers;
using System.Text.Json;

namespace Profiler.Web.Connectors;

public class GitHubConnector : IConnector
{
    private readonly HttpClient _http;
    private readonly string _username;
    private readonly string? _token;

    public string Name => "GitHub";

    public GitHubConnector(HttpClient http, string username, string? token = null)
    {
        _http = http;
        _username = username;
        _token = token;
    }

    private HttpRequestMessage BuildRequest(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("User-Agent", "Profiler.Web/1.0");
        req.Headers.Add("Accept", "application/vnd.github+json");
        if (!string.IsNullOrEmpty(_token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return req;
    }

    public async Task<ProfileData> FetchAsync()
    {
        var features = new List<string>();

        try
        {
            // User info
            using var userResp = await _http.SendAsync(BuildRequest($"https://api.github.com/users/{_username}"));
            if (!userResp.IsSuccessStatusCode)
                throw new ConnectorException($"GitHub API returned {userResp.StatusCode} for user {_username}");

            var userJson = await userResp.Content.ReadAsStringAsync();
            using var userDoc = JsonDocument.Parse(userJson);
            var userRoot = userDoc.RootElement;

            if (userRoot.TryGetProperty("public_gists", out var gists) && gists.GetInt32() > 0)
                features.Add("activity:public-gist");

            // Repos
            using var reposResp = await _http.SendAsync(BuildRequest($"https://api.github.com/users/{_username}/repos?per_page=100"));
            if (reposResp.IsSuccessStatusCode)
            {
                var reposJson = await reposResp.Content.ReadAsStringAsync();
                using var reposDoc = JsonDocument.Parse(reposJson);
                bool hasForks = false;
                foreach (var repo in reposDoc.RootElement.EnumerateArray())
                {
                    if (repo.TryGetProperty("fork", out var fork) && fork.GetBoolean())
                        hasForks = true;

                    if (repo.TryGetProperty("language", out var lang) && lang.ValueKind == JsonValueKind.String)
                    {
                        var langStr = lang.GetString();
                        if (!string.IsNullOrEmpty(langStr))
                            features.Add($"language:{langStr.ToLowerInvariant()}");
                    }

                    if (repo.TryGetProperty("topics", out var topics))
                    {
                        foreach (var topic in topics.EnumerateArray())
                        {
                            var t = topic.GetString();
                            if (!string.IsNullOrEmpty(t))
                                features.Add($"topic:{t}");
                        }
                    }
                }
                if (hasForks) features.Add("repo-type:forked");
            }

            // Starred
            using var starredResp = await _http.SendAsync(BuildRequest($"https://api.github.com/users/{_username}/starred?per_page=100"));
            if (starredResp.IsSuccessStatusCode)
            {
                var starredJson = await starredResp.Content.ReadAsStringAsync();
                using var starredDoc = JsonDocument.Parse(starredJson);
                foreach (var repo in starredDoc.RootElement.EnumerateArray())
                {
                    if (repo.TryGetProperty("topics", out var topics))
                    {
                        foreach (var topic in topics.EnumerateArray())
                        {
                            var t = topic.GetString();
                            if (!string.IsNullOrEmpty(t))
                                features.Add($"starred-topic:{t}");
                        }
                    }
                }
            }
        }
        catch (ConnectorException) { throw; }
        catch (Exception ex) { throw new ConnectorException("GitHub connector error", ex); }

        return new ProfileData("GitHub", features.Distinct().ToList());
    }
}
