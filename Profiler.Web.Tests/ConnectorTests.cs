using Profiler.Web.Connectors;
using Xunit;

namespace Profiler.Web.Tests;

public class ConnectorTests
{
    [Fact]
    public async Task GoodreadsConnector_ParsesGenresAndShelves()
    {
        const string csv = """
            Title,Author,ISBN,My Rating,Bookshelves
            Dune,Frank Herbert,9780441013593,5,"science-fiction,read"
            Harry Potter,J.K. Rowling,9780439708180,4,"fantasy,favorites"
            Unknown Book,Someone,123,3,"some-shelf"
            """;

        var connector = new GoodreadsConnector(csv);
        var data = await connector.FetchAsync();

        Assert.Equal("Goodreads", data.Source);
        Assert.Contains("genre:science-fiction", data.Features);
        Assert.Contains("genre:fantasy", data.Features);
        Assert.Contains("shelf:read", data.Features);
        Assert.Contains("shelf:favorites", data.Features);
        // rating-high for 4 and 5 stars
        Assert.Contains(data.Features, f => f.StartsWith("rating-high:"));
        // Unknown shelf not included as genre
        Assert.DoesNotContain("genre:some-shelf", data.Features);
    }

    [Fact]
    public async Task NetflixConnector_ParsesTitlesAndTypes()
    {
        const string csv = """
            Title,Date
            Breaking Bad: Season 1,2024-01-01
            Inception,2024-01-02
            Planet Earth: Documentary,2024-01-03
            """;

        var connector = new NetflixConnector(csv);
        var data = await connector.FetchAsync();

        Assert.Equal("Netflix", data.Source);
        Assert.Contains("netflix-type:series", data.Features);
        Assert.Contains("netflix-type:movie", data.Features);
        Assert.Contains("netflix-genre:documentary", data.Features);
        Assert.Contains(data.Features, f => f.StartsWith("netflix-watched:"));
    }

    [Fact]
    public async Task GoodreadsConnector_EmptyCsv_NoFeatures()
    {
        const string csv = "Title,Author,My Rating,Bookshelves\n";
        var connector = new GoodreadsConnector(csv);
        var data = await connector.FetchAsync();
        Assert.Empty(data.Features);
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
        var connector = new NetflixConnector(csv);
        var data = await connector.FetchAsync();
        // Should have both series and movie types
        Assert.Contains("netflix-type:series", data.Features);
        Assert.Contains("netflix-type:movie", data.Features);
    }
}
