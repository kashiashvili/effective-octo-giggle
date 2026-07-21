using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace Profiler.Web.Connectors;

public class GoodreadsConnector : IConnector
{
    private readonly string _csvContent;
    public string Name => "Goodreads";

    private static readonly HashSet<string> GenreShelves = new(StringComparer.OrdinalIgnoreCase)
    {
        "fiction", "non-fiction", "science-fiction", "fantasy", "mystery", "thriller",
        "romance", "biography", "history", "self-help", "science", "philosophy",
        "poetry", "horror", "young-adult", "children", "graphic-novels", "travel",
        "cooking", "art", "religion", "psychology", "business", "technology"
    };

    private static readonly HashSet<string> TasteShelves = new(StringComparer.OrdinalIgnoreCase)
    {
        "read", "currently-reading", "favorites"
    };

    public GoodreadsConnector(string csvContent)
    {
        _csvContent = csvContent;
    }

    private static string TitleHash(string title)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(title.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

    public Task<ProfileData> FetchAsync(CancellationToken cancellationToken = default)
    {
        var features = new List<string>();

        try
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null
            };
            using var reader = new StringReader(_csvContent);
            using var csv = new CsvReader(reader, config);
            csv.Read();
            csv.ReadHeader();

            while (csv.Read())
            {
                var title = csv.TryGetField<string>("Title", out var t) ? t ?? "" : "";
                var shelves = csv.TryGetField<string>("Bookshelves", out var s) ? s ?? "" : "";
                if (string.IsNullOrEmpty(shelves))
                    shelves = csv.TryGetField<string>("Exclusive Shelf", out var es) ? es ?? "" : "";

                var ratingStr = csv.TryGetField<string>("My Rating", out var r) ? r ?? "0" : "0";
                int.TryParse(ratingStr, out int rating);

                foreach (var shelf in shelves.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (GenreShelves.Contains(shelf))
                        features.Add($"genre:{shelf.ToLowerInvariant()}");
                    if (TasteShelves.Contains(shelf))
                        features.Add($"shelf:{shelf.ToLowerInvariant()}");
                }

                if (rating >= 4 && !string.IsNullOrEmpty(title))
                    features.Add($"rating-high:{TitleHash(title)}");
            }
        }
        catch (Exception ex)
        {
            throw new ConnectorException("Goodreads CSV parse error", ex);
        }

        return Task.FromResult(new ProfileData("Goodreads", features.Distinct().ToList()));
    }
}
