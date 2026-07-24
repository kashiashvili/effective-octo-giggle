namespace Profiler.Web.ViewModels;

/// <summary>
/// The values questionnaire form. One 1–5 answer per item, keyed by item key. Bound as a dictionary
/// so the view can render the item list without a fixed field per item, and so the raw answers stay
/// together in one place that the controller derives from and then drops.
/// </summary>
public class ValuesViewModel
{
    /// <summary>Item key → 1–5 answer. Empty/partial is caught in the controller against the item set.</summary>
    public Dictionary<string, int> Answers { get; set; } = new();

    /// <summary>
    /// Consent checkbox — the signal is not saved unless the user explicitly opts in. Enforced in the
    /// controller (not by a validation attribute), so there is a single consent error, not two.
    /// </summary>
    public bool Consent { get; set; }
}
