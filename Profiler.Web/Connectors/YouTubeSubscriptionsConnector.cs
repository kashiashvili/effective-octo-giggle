using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace Profiler.Web.Connectors;

/// <summary>
/// YouTube without a token: the <c>subscriptions.csv</c> from a Google Takeout export ("YouTube and
/// YouTube Music" → subscriptions). Emits the same <c>youtube-channel:&lt;slug&gt;</c> features as the
/// token-based <see cref="YouTubeConnector"/>, so someone who uploaded their export and someone who
/// pasted a token still match on the channels they share. Parsed in memory and discarded, like every
/// other CSV; only the derived signature is kept.
/// </summary>
public class YouTubeSubscriptionsConnector : IConnector
{
    /// <summary>
    /// Takeout lists every subscription while the token path sees at most 30; wide sets dilute the
    /// Jaccard overlap, so the export is capped too — generously — at the first distinct channels.
    /// </summary>
    public const int MaxChannels = 50;

    private readonly string _csvContent;
    public string Name => "YouTube";

    public YouTubeSubscriptionsConnector(string csvContent) => _csvContent = csvContent;

    public Task<ProfileData> FetchAsync(CancellationToken cancellationToken = default)
    {
        var features = new List<string>();
        try
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null,
                BadDataFound = null
            };
            using var reader = new StringReader(_csvContent);
            using var csv = new CsvReader(reader, config);
            if (csv.Read())
            {
                csv.ReadHeader();
                // Takeout localises the header row ("Channel Title", "Kanaltitel", …) but the title is
                // always the third column; the name is tried first, the position is the fallback.
                var titleIndex = Array.FindIndex(csv.HeaderRecord ?? [],
                    h => string.Equals(h?.Trim().TrimStart('\uFEFF'), "Channel Title", StringComparison.OrdinalIgnoreCase));
                if (titleIndex < 0) titleIndex = 2;

                while (features.Count < MaxChannels && csv.Read())
                {
                    var title = csv.TryGetField<string>(titleIndex, out var t) ? t?.Trim() ?? "" : "";
                    if (title.Length == 0) continue;
                    // Same slug as the token connector, same limitation: titles without Latin letters or
                    // digits slug to nothing and are skipped.
                    var slug = YouTubeConnector.Slugify(title);
                    if (slug.Length == 0) continue;
                    var feature = $"youtube-channel:{slug}";
                    if (!features.Contains(feature)) features.Add(feature);
                }
            }
        }
        catch (Exception ex)
        {
            throw new ConnectorException("YouTube subscriptions CSV parse error", ex);
        }

        return Task.FromResult(new ProfileData(Name, features));
    }
}
