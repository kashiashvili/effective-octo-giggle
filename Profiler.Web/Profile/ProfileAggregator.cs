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
    /// <summary>
    /// How long the whole fan-out may take. Each connector's HTTP client already gives up after 15s,
    /// but a connector that makes several calls can outlast that, and the user is staring at a
    /// blocking POST the entire time. Past this point the sources that did answer are kept and the
    /// rest are reported as timed out, rather than the gateway cutting the request and losing
    /// everything — including uploaded CSVs, which a browser cannot repopulate.
    /// </summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private readonly IEnumerable<IConnector> _connectors;
    private readonly TimeSpan _budget;

    public ProfileAggregator(IEnumerable<IConnector> connectors) : this(connectors, Budget) { }

    public ProfileAggregator(IEnumerable<IConnector> connectors, TimeSpan budget)
    {
        _connectors = connectors;
        _budget = budget;
    }

    public async Task<AggregationResult> AggregateAsync()
    {
        var result = new AggregationResult();
        var connectors = _connectors.ToList();

        // Connectors are independent, so they run together: connecting five sources used to cost the
        // sum of five timeouts. Results are collected in the caller's order afterwards, so what the
        // user sees does not depend on which network call happened to answer first.
        var attempts = connectors.Select(FetchAsync).ToList();

        using var expiry = new CancellationTokenSource();
        var budget = Task.Delay(_budget, expiry.Token);
        var completed = Task.WhenAll(attempts);
        if (await Task.WhenAny(completed, budget) == completed) expiry.Cancel();

        for (var i = 0; i < connectors.Count; i++)
        {
            var attempt = attempts[i];
            // FetchAsync catches everything, so a finished attempt cannot be faulted — but reading
            // .Result on a faulted task would throw here and lose every other source's work, so the
            // check is for success rather than merely completion.
            if (!attempt.IsCompletedSuccessfully)
            {
                // Abandoned rather than aborted: the connector's own client timeout ends it shortly.
                result.Failures.Add(new ConnectorFailure(connectors[i].Name, "took too long to respond"));
                continue;
            }

            var outcome = attempt.Result;
            if (outcome.Data != null)
                result.Results.Add(new SourceResult(outcome.Data.Source, outcome.Data.Features.ToList()));
            else
                result.Failures.Add(new ConnectorFailure(connectors[i].Name, outcome.Error!));
        }

        return result;
    }

    private static async Task<Attempt> FetchAsync(IConnector connector)
    {
        try
        {
            var data = await connector.FetchAsync();
            if (data.Features.Count == 0)
            {
                // Connectors that swallow HTTP errors surface here as "success with no data";
                // either way the source contributed nothing and must not be listed as connected.
                return new Attempt(null, "returned no interest data — check the credentials or account name");
            }
            return new Attempt(data, null);
        }
        catch (ConnectorException ex)
        {
            return new Attempt(null, ex.Message);
        }
        catch (Exception ex)
        {
            return new Attempt(null, $"Unexpected error: {ex.Message}");
        }
    }

    private record Attempt(ProfileData? Data, string? Error);
}
