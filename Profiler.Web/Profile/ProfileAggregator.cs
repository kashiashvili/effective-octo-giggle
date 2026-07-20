using Profiler.Web.Connectors;

namespace Profiler.Web.Profile;

public class ProfileAggregator
{
    private readonly IEnumerable<IConnector> _connectors;
    private readonly bool _skipErrors;

    public ProfileAggregator(IEnumerable<IConnector> connectors, bool skipErrors = true)
    {
        _connectors = connectors;
        _skipErrors = skipErrors;
    }

    public async Task<(List<string> sources, List<string> features)> AggregateAsync()
    {
        var sources = new List<string>();
        var features = new List<string>();

        foreach (var connector in _connectors)
        {
            try
            {
                var data = await connector.FetchAsync();
                sources.Add(data.Source);
                features.AddRange(data.Features);
            }
            catch (Exception)
            {
                if (!_skipErrors) throw;
            }
        }

        return (sources, features);
    }
}
