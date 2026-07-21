using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Profiler.Web.Connectors;

public class TwitterConnector : IConnector
{
    private readonly HttpClient _http;
    private readonly string _bearerToken;
    public string Name => "Twitter/X";

    public TwitterConnector(HttpClient http, string bearerToken)
    {
        _http = http;
        _bearerToken = bearerToken;
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
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
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

        using var me = await TryGetJson("https://api.twitter.com/2/users/me", cancellationToken);
        if (me == null) return new ProfileData("Twitter/X", features.ToList());

        string? userId = null;
        if (me.RootElement.TryGetProperty("data", out var meData) &&
            meData.TryGetProperty("id", out var idProp))
            userId = idProp.GetString();

        if (string.IsNullOrEmpty(userId))
            return new ProfileData("Twitter/X", features.ToList());

        using var liked = await TryGetJson(
            $"https://api.twitter.com/2/users/{userId}/liked_tweets?max_results=100&tweet.fields=entities,context_annotations", cancellationToken);

        if (liked != null && liked.RootElement.TryGetProperty("data", out var tweets))
        {
            foreach (var tweet in tweets.EnumerateArray())
            {
                if (tweet.TryGetProperty("context_annotations", out var annotations))
                {
                    foreach (var annotation in annotations.EnumerateArray())
                    {
                        if (annotation.TryGetProperty("entity", out var entity) &&
                            entity.TryGetProperty("name", out var entityName))
                        {
                            var name = entityName.GetString();
                            if (!string.IsNullOrEmpty(name))
                                features.Add($"twitter-topic:{Slugify(name)}");
                        }
                    }
                }

                if (tweet.TryGetProperty("entities", out var entities) &&
                    entities.TryGetProperty("hashtags", out var hashtags))
                {
                    foreach (var hashtag in hashtags.EnumerateArray())
                    {
                        if (hashtag.TryGetProperty("tag", out var tag))
                        {
                            var t = tag.GetString();
                            if (!string.IsNullOrEmpty(t))
                                features.Add($"twitter-hashtag:{t.ToLowerInvariant()}");
                        }
                    }
                }
            }
        }

        return new ProfileData("Twitter/X", features.ToList());
    }
}
