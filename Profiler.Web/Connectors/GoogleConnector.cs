using System.Net.Http.Headers;
using System.Text.Json;

namespace Profiler.Web.Connectors;

public class GoogleConnector : IConnector
{
    private readonly HttpClient _http;
    private readonly string _token;
    public string Name => "Google";

    public GoogleConnector(HttpClient http, string token)
    {
        _http = http;
        _token = token;
    }

    public async Task<ProfileData> FetchAsync(CancellationToken cancellationToken = default)
    {
        var features = new List<string>();

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                "https://people.googleapis.com/v1/people/me?personFields=interests,locales,organizations");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Headers.Add("User-Agent", "Profiler.Web/1.0");

            using var resp = await _http.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
                throw new ConnectorException($"Google API returned {resp.StatusCode}");

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("interests", out var interests))
                foreach (var item in interests.EnumerateArray())
                    if (item.TryGetProperty("value", out var v))
                        features.Add($"interest:{v.GetString()?.ToLowerInvariant()}");

            if (root.TryGetProperty("locales", out var locales))
                foreach (var item in locales.EnumerateArray())
                    if (item.TryGetProperty("value", out var v))
                        features.Add($"locale:{v.GetString()?.ToLowerInvariant()}");

            if (root.TryGetProperty("organizations", out var orgs))
                foreach (var item in orgs.EnumerateArray())
                    if (item.TryGetProperty("type", out var v))
                        features.Add($"org-type:{v.GetString()?.ToLowerInvariant()}");
        }
        catch (ConnectorException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new ConnectorException("Google connector error", ex); }

        return new ProfileData("Google", features.Distinct().ToList());
    }
}
