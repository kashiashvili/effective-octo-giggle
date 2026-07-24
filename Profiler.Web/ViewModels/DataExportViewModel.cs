namespace Profiler.Web.ViewModels;

/// <summary>Everything Profiler stores about a single user — used for the transparency page and JSON export.</summary>
public class DataExportViewModel
{
    public string Username { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsDiscoverable { get; set; }
    public string? Bio { get; set; }
    public string? Contact { get; set; }

    /// <summary>The stored connection-intent key, or null if unspecified.</summary>
    public string? ConnectionIntent { get; set; }

    /// <summary>The derived values bucket (−2..+2), or null. The raw answers are never stored.</summary>
    public int? ValuesOpenness { get; set; }

    /// <summary>The scheme version the values bucket was derived under, or null.</summary>
    public string? ValuesScheme { get; set; }

    /// <summary>When the user last opened their matches (drives the "new since last visit" count).</summary>
    public DateTime? LastMatchesViewedAt { get; set; }

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
