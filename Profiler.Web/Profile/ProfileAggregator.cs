using Profiler.Web.Connectors;

namespace Profiler.Web.Profile;

public record ConnectorFailure(string Source, string Message);

public record SourceResult(string Source, List<string> Features);

public class AggregationResult
{
    public List<SourceResult> Results { get; } = new();
    public List<ConnectorFailure> Failures { get; } = new();

    public List<string> Sources => Results.Select(r => r.Source).ToList();
    public List<string> Features => Results.SelectMany(r => r.Features).ToList();
}

public class ProfileAggregator
{
    private readonly IEnumerable<IConnector> _connectors;

    public ProfileAggregator(IEnumerable<IConnector> connectors)
    {
        _connectors = connectors;
    }

    public async Task<AggregationResult> AggregateAsync()
    {
        var result = new AggregationResult();

        foreach (var connector in _connectors)
        {
            try
            {
                var data = await connector.FetchAsync();
                if (data.Features.Count == 0)
                {
                    // Connectors that swallow HTTP errors surface here as "success with no data";
                    // either way the source contributed nothing and must not be listed as connected.
                    result.Failures.Add(new ConnectorFailure(connector.Name,
                        "returned no interest data — check the credentials or account name"));
                    continue;
                }
                result.Results.Add(new SourceResult(data.Source, data.Features.ToList()));
            }
            catch (ConnectorException ex)
            {
                result.Failures.Add(new ConnectorFailure(connector.Name, ex.Message));
            }
            catch (Exception ex)
            {
                result.Failures.Add(new ConnectorFailure(connector.Name, $"Unexpected error: {ex.Message}"));
            }
        }

        return result;
    }
}
