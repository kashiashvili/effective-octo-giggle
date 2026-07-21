using System.Net;
using Profiler.Web.Connectors;
using Xunit;

namespace Profiler.Web.Tests;

public class ConnectorTests
{
    /// <summary>
    /// Returns a canned HTTP response for every request, so connector tests are deterministic and
    /// offline instead of hitting real third-party APIs over the network.
    /// </summary>
    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        private readonly string _body;

        public StubHttpHandler(HttpStatusCode code, string body = "{}")
        {
            _code = code;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_code) { Content = new StringContent(_body) });
    }

    private static HttpClient StubClient(HttpStatusCode code, string body = "{}")
        => new(new StubHttpHandler(code, body));

    /// <summary>Routes each request to a canned response by the first URL substring that matches.</summary>
    private sealed class RoutingStubHandler : HttpMessageHandler
    {
        private readonly List<(string needle, HttpStatusCode code, string body)> _routes = new();

        public RoutingStubHandler When(string urlContains, string body, HttpStatusCode code = HttpStatusCode.OK)
        {
            _routes.Add((urlContains, code, body));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            foreach (var (needle, code, body) in _routes)
                if (url.Contains(needle))
                    return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
        }
    }

    // ---- Goodreads CSV --------------------------------------------------

    [Fact]
    public async Task GoodreadsConnector_ParsesGenresAndShelves()
    {
        const string csv = """
            Title,Author,ISBN,My Rating,Bookshelves
            Dune,Frank Herbert,9780441013593,5,"science-fiction,read"
            Harry Potter,J.K. Rowling,9780439708180,4,"fantasy,favorites"
            Unknown Book,Someone,123,3,"some-shelf"
            """;

        var data = await new GoodreadsConnector(csv).FetchAsync();

        Assert.Equal("Goodreads", data.Source);
        Assert.Contains("genre:science-fiction", data.Features);
        Assert.Contains("genre:fantasy", data.Features);
        Assert.Contains("shelf:read", data.Features);
        Assert.Contains("shelf:favorites", data.Features);
        Assert.Contains(data.Features, f => f.StartsWith("rating-high:"));
        Assert.DoesNotContain("genre:some-shelf", data.Features);
    }

    [Fact]
    public async Task GoodreadsConnector_EmptyCsv_NoFeatures()
    {
        var data = await new GoodreadsConnector("Title,Author,My Rating,Bookshelves\n").FetchAsync();
        Assert.Empty(data.Features);
    }

    [Fact]
    public async Task GoodreadsConnector_GarbageOrEmpty_NeverThrowsRaw()
    {
        // A user can upload the wrong file. The aggregator only guards against ConnectorException,
        // so nothing else may escape.
        foreach (var content in new[] { "", "this is not a csv at all", "\n\n\n" })
        {
            var ex = await Record.ExceptionAsync(() => new GoodreadsConnector(content).FetchAsync());
            Assert.True(ex is null or ConnectorException, $"Unexpected exception for {content.Length}-char input: {ex}");
        }
    }

    // ---- Netflix CSV ----------------------------------------------------

    [Fact]
    public async Task NetflixConnector_ParsesTitlesAndTypes()
    {
        const string csv = """
            Title,Date
            Breaking Bad: Season 1,2024-01-01
            Inception,2024-01-02
            Planet Earth: Documentary,2024-01-03
            """;

        var data = await new NetflixConnector(csv).FetchAsync();

        Assert.Equal("Netflix", data.Source);
        Assert.Contains("netflix-type:series", data.Features);
        Assert.Contains("netflix-type:movie", data.Features);
        Assert.Contains("netflix-genre:documentary", data.Features);
        Assert.Contains(data.Features, f => f.StartsWith("netflix-watched:"));
    }

    [Fact]
    public async Task NetflixConnector_SeriesDetection()
    {
        const string csv = """
            Title,Date
            Stranger Things S02E01,2024-01-01
            The Crown: Season 3,2024-01-02
            Titanic,2024-01-03
            """;
        var data = await new NetflixConnector(csv).FetchAsync();
        Assert.Contains("netflix-type:series", data.Features);
        Assert.Contains("netflix-type:movie", data.Features);
    }

    [Fact]
    public async Task NetflixConnector_GarbageOrEmpty_NeverThrowsRaw()
    {
        foreach (var content in new[] { "", "random text, not netflix", "\n" })
        {
            var ex = await Record.ExceptionAsync(() => new NetflixConnector(content).FetchAsync());
            Assert.True(ex is null or ConnectorException, $"Unexpected exception for {content.Length}-char input: {ex}");
        }
    }

    // ---- API connectors: graceful failure (offline via stub) ------------

    /// <summary>Returns a redirect for the first URL, then a canned body for the target.</summary>
    private sealed class RedirectStubHandler : HttpMessageHandler
    {
        private readonly string _redirectTo;
        private readonly string _finalBody;
        public int RequestsToTarget { get; private set; }

        public RedirectStubHandler(string redirectTo, string finalBody)
        {
            _redirectTo = redirectTo;
            _finalBody = finalBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url == _redirectTo)
            {
                RequestsToTarget++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_finalBody) });
            }
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri(_redirectTo);
            return Task.FromResult(redirect);
        }
    }

    private const string SampleRss =
        "<rss><channel><title>Test Feed</title><item><title>Rust ownership explained</title>" +
        "<category>programming</category></item></channel></rss>";

    [Fact]
    public async Task RssConnector_DoesNotResolveExternalEntities_Xxe()
    {
        const string xxe =
            "<?xml version=\"1.0\"?>" +
            "<!DOCTYPE foo [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>" +
            "<rss><channel><title>&xxe;</title><item><title>&xxe;</title></item></channel></rss>";

        // .NET resolves external entities through XmlResolver, which defaults to null — the DTD is
        // accepted but the entity expands to nothing, so no file is ever read.
        var title = System.Xml.Linq.XDocument.Parse(xxe).Descendants("title").First().Value;
        Assert.Equal("", title);

        var data = await new RssFeedsConnector(StubClient(HttpStatusCode.OK, xxe), "https://8.8.8.8/feed").FetchAsync();

        // Nothing from the referenced file may reach the extracted features.
        Assert.DoesNotContain(data.Features, f => f.Contains("root") || f.Contains("bin/") || f.Contains("passwd"));
    }

    [Fact]
    public async Task RssConnector_DoesNotFollowRedirectToInternalAddress()
    {
        var handler = new RedirectStubHandler("http://169.254.169.254/latest/meta-data", SampleRss);
        var data = await new RssFeedsConnector(new HttpClient(handler), "https://8.8.8.8/feed").FetchAsync();

        Assert.Empty(data.Features);
        Assert.Equal(0, handler.RequestsToTarget);
    }

    [Fact]
    public async Task RssConnector_FollowsRedirectToPublicAddress()
    {
        var handler = new RedirectStubHandler("https://1.1.1.1/real-feed", SampleRss);
        var data = await new RssFeedsConnector(new HttpClient(handler), "https://8.8.8.8/feed").FetchAsync();

        Assert.Equal(1, handler.RequestsToTarget);
        Assert.Contains("rss-feed:test-feed", data.Features);
        Assert.Contains("rss-topic:programming", data.Features);
    }

    [Fact]
    public async Task RssConnector_SkipsInternalFeedUrlEntirely()
    {
        var handler = new RedirectStubHandler("https://1.1.1.1/x", SampleRss);
        var data = await new RssFeedsConnector(new HttpClient(handler), "http://127.0.0.1/feed").FetchAsync();

        Assert.Empty(data.Features);
        Assert.Equal(0, handler.RequestsToTarget);
    }

    [Fact]
    public async Task GitHubConnector_ExtractsLanguagesTopicsForksAndStars()
    {
        // Order matters: /repos and /starred also contain "/users/", so match them first.
        var handler = new RoutingStubHandler()
            .When("/starred", "[{\"topics\":[\"machine-learning\"]}]")
            .When("/repos", "[{\"fork\":true,\"language\":\"C\",\"topics\":[\"kernel\"]}," +
                            "{\"fork\":false,\"language\":\"Python\",\"topics\":[]}]")
            .When("/users/", "{\"public_gists\":3}");

        var data = await new GitHubConnector(new HttpClient(handler), "torvalds").FetchAsync();

        Assert.Equal("GitHub", data.Source);
        Assert.Contains("activity:public-gist", data.Features);
        Assert.Contains("language:c", data.Features);
        Assert.Contains("language:python", data.Features);
        Assert.Contains("topic:kernel", data.Features);
        Assert.Contains("repo-type:forked", data.Features);
        Assert.Contains("starred-topic:machine-learning", data.Features);
    }

    [Fact]
    public async Task GitHubConnector_UnknownUser_ThrowsConnectorException()
    {
        var http = StubClient(HttpStatusCode.NotFound);
        await Assert.ThrowsAsync<ConnectorException>(() => new GitHubConnector(http, "nobody").FetchAsync());
    }

    [Fact]
    public async Task RedditConnector_ExtractsSubreddits_OnSuccess()
    {
        const string body = """
            {"data":{"children":[
                {"data":{"display_name":"rust"}},
                {"data":{"display_name":"MechanicalKeyboards"}}
            ]}}
            """;
        var data = await new RedditConnector(StubClient(HttpStatusCode.OK, body), "token").FetchAsync();

        Assert.Contains("reddit-sub:rust", data.Features);
        Assert.Contains("reddit-sub:mechanicalkeyboards", data.Features);
    }

    [Fact]
    public async Task SpotifyConnector_AuthError_ReturnsEmpty()
    {
        var data = await new SpotifyConnector(StubClient(HttpStatusCode.Unauthorized), "bad-token").FetchAsync();
        Assert.Equal("Spotify", data.Source);
        Assert.Empty(data.Features);
    }

    [Fact]
    public async Task SpotifyConnector_ParsesTopArtists_OnSuccess()
    {
        const string body = """
            {"items":[{"name":"Radiohead","genres":["art rock","alternative rock"]}]}
            """;
        var data = await new SpotifyConnector(StubClient(HttpStatusCode.OK, body), "token").FetchAsync();

        Assert.Contains("spotify-artist:radiohead", data.Features);
        Assert.Contains("spotify-genre:art-rock", data.Features);
    }

    [Fact]
    public async Task RedditConnector_AuthError_ReturnsEmpty()
    {
        var data = await new RedditConnector(StubClient(HttpStatusCode.Forbidden), "bad-token").FetchAsync();
        Assert.Equal("Reddit", data.Source);
        Assert.Empty(data.Features);
    }

    [Fact]
    public async Task LastFmConnector_AuthError_ReturnsEmpty()
    {
        var data = await new LastFmConnector(StubClient(HttpStatusCode.Forbidden), "key", "user").FetchAsync();
        Assert.Equal("Last.fm", data.Source);
        Assert.Empty(data.Features);
    }

    [Fact]
    public async Task SteamConnector_AuthError_ReturnsEmpty()
    {
        var data = await new SteamConnector(StubClient(HttpStatusCode.Forbidden), "key", "76561198000000000").FetchAsync();
        Assert.Equal("Steam", data.Source);
        Assert.Empty(data.Features);
    }

    [Fact]
    public async Task TikTokConnector_AuthError_ReturnsEmpty()
    {
        var data = await new TikTokConnector(StubClient(HttpStatusCode.Unauthorized), "bad-token").FetchAsync();
        Assert.Equal("TikTok", data.Source);
        Assert.Empty(data.Features);
    }

    [Fact]
    public async Task YouTubeConnector_AuthError_ReturnsEmpty()
    {
        var data = await new YouTubeConnector(StubClient(HttpStatusCode.Unauthorized), "bad-token").FetchAsync();
        Assert.Equal("YouTube", data.Source);
        Assert.Empty(data.Features);
    }

    [Fact]
    public async Task InstagramConnector_AuthError_ReturnsEmpty()
    {
        var data = await new InstagramConnector(StubClient(HttpStatusCode.Unauthorized), "bad-token").FetchAsync();
        Assert.Equal("Instagram", data.Source);
        Assert.Empty(data.Features);
    }

    [Fact]
    public async Task RssFeedsConnector_InvalidFeeds_ReturnsEmptyWithoutThrowing()
    {
        var data = await new RssFeedsConnector(StubClient(HttpStatusCode.OK, "<html>not a feed</html>"),
            "not-a-url\nhttps://example.com/feed").FetchAsync();
        Assert.Equal("RSS/Blogs", data.Source);
        Assert.NotNull(data.Features);
    }
}
