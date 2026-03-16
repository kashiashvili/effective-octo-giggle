using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Profiler.Web.Connectors;

public class LinkedInConnector : IConnector
{
    private readonly HttpClient _http;
    private readonly string _accessToken;
    public string Name => "LinkedIn";

    public LinkedInConnector(HttpClient http, string accessToken)
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

    private async Task<JsonDocument?> TryGetJson(string url)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            req.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            return JsonDocument.Parse(json);
        }
        catch { return null; }
    }

    public async Task<ProfileData> FetchAsync()
    {
        var features = new HashSet<string>();

        using var skills = await TryGetJson(
            "https://api.linkedin.com/v2/skillsV2?q=memberAndSkill&projection=(elements*(name,proficiencyLevel))");
        if (skills != null && skills.RootElement.TryGetProperty("elements", out var skillElements))
        {
            foreach (var skill in skillElements.EnumerateArray())
            {
                if (skill.TryGetProperty("name", out var nameProp))
                {
                    var name = nameProp.GetString();
                    if (!string.IsNullOrEmpty(name))
                        features.Add($"linkedin-skill:{Slugify(name)}");
                }
            }
        }

        using var positions = await TryGetJson(
            "https://api.linkedin.com/v2/positions?q=members&projection=(elements*(title,company~(name,industries~)))");
        if (positions != null && positions.RootElement.TryGetProperty("elements", out var posElements))
        {
            foreach (var pos in posElements.EnumerateArray())
            {
                if (pos.TryGetProperty("title", out var titleProp))
                {
                    var title = titleProp.GetString();
                    if (!string.IsNullOrEmpty(title))
                    {
                        var firstWord = title.Split(' ')[0];
                        features.Add($"linkedin-title:{Slugify(firstWord)}");
                    }
                }

                if (pos.TryGetProperty("company~", out var company) &&
                    company.TryGetProperty("industries~", out var industries) &&
                    industries.TryGetProperty("elements", out var industryElements))
                {
                    foreach (var industry in industryElements.EnumerateArray())
                    {
                        if (industry.TryGetProperty("name", out var indName))
                        {
                            var n = indName.GetString();
                            if (!string.IsNullOrEmpty(n))
                                features.Add($"linkedin-industry:{Slugify(n)}");
                        }
                    }
                }
            }
        }

        return new ProfileData("LinkedIn", features.ToList());
    }
}
