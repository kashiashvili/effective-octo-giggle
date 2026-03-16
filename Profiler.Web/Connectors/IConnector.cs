namespace Profiler.Web.Connectors;

public interface IConnector
{
    string Name { get; }
    Task<ProfileData> FetchAsync();
}
