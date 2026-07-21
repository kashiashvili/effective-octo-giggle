namespace Profiler.Web.ViewModels;

/// <summary>Everything Profiler stores about a single user — used for the transparency page and JSON export.</summary>
public class DataExportViewModel
{
    public string Username { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsDiscoverable { get; set; }
    public string? Bio { get; set; }
    public string? Contact { get; set; }

    /// <summary>Number of dimensions in the stored MinHash signature (the signature is anonymized).</summary>
    public int FingerprintDimensions { get; set; }

    public List<DataExportSource> Sources { get; set; } = new();

    /// <summary>
    /// The people you have hidden. Only your own outgoing hides — who hid *you* is deliberately not
    /// listed, because telling someone they were hidden would defeat the point of the control.
    /// </summary>
    public List<string> HiddenPeople { get; set; } = new();

    /// <summary>Always true — stated explicitly so the export makes the privacy guarantee legible.</summary>
    public bool RawInterestsStored => false;
}

public class DataExportSource
{
    public string Source { get; set; } = "";
    public int SignalCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}
