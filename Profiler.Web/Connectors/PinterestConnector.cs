using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Profiler.Web.Connectors;

public class PinterestConnector : IConnector
{
    private readonly HttpClient _http;
    private readonly string _token;
    public string Name => "Pinterest";

    public PinterestConnector(HttpClient http, string token)
    {
        _http = http;
        _token = token;
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
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
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
        var features = new List<string>();

        using var boards = await TryGetJson("https://api.pinterest.com/v5/boards", cancellationToken);
        if (boards != null && boards.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var board in items.EnumerateArray())
            {
                var name = "";
                if (board.TryGetProperty("name", out var n)) name = n.GetString() ?? "";
                if (!string.IsNullOrEmpty(name))
                    features.Add($"pinterest-board:{Slugify(name)}");

                if (board.TryGetProperty("description", out var desc))
                {
                    var d = desc.GetString() ?? "";
                    if (!string.IsNullOrEmpty(d))
                        features.Add($"pinterest-topic:{Slugify(d[..Math.Min(d.Length, 50)])}");
                }
            }
        }

        // Boards the user is following (Pinterest API v5: /user_account/following/boards)
        using var followed = await TryGetJson("https://api.pinterest.com/v5/user_account/following/boards", cancellationToken);
        if (followed != null && followed.RootElement.TryGetProperty("items", out var fItems))
        {
            foreach (var board in fItems.EnumerateArray())
            {
                var name = "";
                if (board.TryGetProperty("name", out var n)) name = n.GetString() ?? "";
                if (!string.IsNullOrEmpty(name))
                    features.Add($"pinterest-following-board:{Slugify(name)}");
            }
        }

        return new ProfileData("Pinterest", features.Distinct().ToList());
    }
}
