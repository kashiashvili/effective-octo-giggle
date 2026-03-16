namespace Profiler.Web.Connectors;

public class ConnectorException : Exception
{
    public ConnectorException(string message) : base(message) { }
    public ConnectorException(string message, Exception inner) : base(message, inner) { }
}
