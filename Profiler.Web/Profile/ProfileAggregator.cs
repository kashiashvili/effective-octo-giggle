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

    /// <summary>
    /// One source can arrive twice in a single submission — YouTube by token and by Takeout export —
    /// while the stored per-source signature is unique per source. Results for the same source are
    /// merged as the union of their features, so both paths feed one "YouTube" row.
    /// </summary>
    public List<SourceResult> MergedBySource() => Results
        .GroupBy(r => r.Source)
        .Select(g => new SourceResult(g.Key, g.SelectMany(r => r.Features).Distinct().ToList()))
        .ToList();
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

    public async Task<AggregationResult> AggregateAsync(CancellationToken cancellationToken = default)
    {
        var result = new AggregationResult();
        var connectors = _connectors.ToList();

        // The budget and the caller going away (the browser tab closing) are two independent reasons
        // to stop, and both now reach the connectors through the same token, rather than only being
        // noticed here after the fact.
        using var budgetCts = new CancellationTokenSource(_budget);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budgetCts.Token);
        var token = linkedCts.Token;

        // Connectors are independent, so they run together: connecting five sources used to cost the
        // sum of five timeouts. Results are collected in the caller's order afterwards, so what the
        // user sees does not depend on which network call happened to answer first.
        var attempts = connectors.Select(c => FetchAsync(c, token)).ToList();

        var completed = Task.WhenAll(attempts);
        // Completes exactly when the token above is cancelled — the budget elapsing or the caller
        // disappearing — purely to bound how long this method itself waits. The connectors already
        // hold the same token, which is what actually stops their outbound calls; this race is the
        // backstop for a connector that ignores it or is stuck somewhere uncancellable.
        var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, token);
        await Task.WhenAny(completed, cancelled);

        for (var i = 0; i < connectors.Count; i++)
        {
            var attempt = attempts[i];
            // FetchAsync catches everything except cancellation, so a finished attempt is either a
            // genuine success or one the token above cut short — reading .Result on a faulted or
            // canceled task would throw here and lose every other source's work, so the check is for
            // success rather than merely completion.
            if (!attempt.IsCompletedSuccessfully)
            {
                // Aborted rather than abandoned: the token passed into FetchAsync already told this
                // connector to stop, so nothing is left running past this method's return.
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

    private static async Task<Attempt> FetchAsync(IConnector connector, CancellationToken cancellationToken)
    {
        try
        {
            var data = await connector.FetchAsync(cancellationToken);
            if (data.Features.Count == 0)
            {
                // Connectors that swallow HTTP errors surface here as "success with no data";
                // either way the source contributed nothing and must not be listed as connected.
                return new Attempt(null, "returned no interest data — check the credentials or account name");
            }
            return new Attempt(data, null);
        }
        catch (OperationCanceledException)
        {
            // The budget (or the caller) ended this connector mid-flight. That is the same outcome
            // as a connector that simply never got to finish, so let it propagate as an incomplete
            // attempt below rather than being caught by the catch-all and reported as an "Unexpected
            // error: The operation was canceled."
            throw;
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
