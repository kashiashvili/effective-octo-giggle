using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;

namespace Profiler.Web.Connectors;

public class NetflixConnector : IConnector
{
    private readonly string _csvContent;
    public string Name => "Netflix";

    private static readonly Regex SeriesPattern = new(@"(Season\s+\d+|Episode\s+\d+|S\d+E\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, string[]> GenreKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["documentary"] = ["documentary", "document", "true crime", "nature", "history"],
        ["comedy"] = ["comedy", "stand-up", "funny", "humor"],
        ["anime"] = ["anime", "animation", "animated"],
        ["thriller"] = ["thriller", "suspense", "mystery"],
        ["sci-fi"] = ["sci-fi", "science fiction", "scifi", "space", "futur"],
        ["reality-tv"] = ["reality", "competition", "survivor", "bachelor", "bake off"],
        ["kids"] = ["kids", "children", "junior", "family", "paw patrol", "peppa"]
    };

    public NetflixConnector(string csvContent)
    {
        _csvContent = csvContent;
    }

    private static string TitleHash(string title)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(title.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

    public Task<ProfileData> FetchAsync()
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
                if (string.IsNullOrEmpty(title)) continue;

                var isSeries = SeriesPattern.IsMatch(title);
                features.Add(isSeries ? "netflix-type:series" : "netflix-type:movie");

                var titleLower = title.ToLowerInvariant();
                foreach (var (genre, keywords) in GenreKeywords)
                {
                    if (keywords.Any(k => titleLower.Contains(k)))
                        features.Add($"netflix-genre:{genre}");
                }

                features.Add($"netflix-watched:{TitleHash(title)}");
            }
        }
        catch (Exception ex)
        {
            throw new ConnectorException("Netflix CSV parse error", ex);
        }

        return Task.FromResult(new ProfileData("Netflix", features.Distinct().ToList()));
    }
}
