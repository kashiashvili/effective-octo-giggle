using System.ComponentModel.DataAnnotations;

namespace Profiler.Web.ViewModels;

public class ProfileViewModel
{
    [MaxLength(280, ErrorMessage = "Bio must be 280 characters or fewer.")]
    [Display(Name = "Bio")]
    public string? Bio { get; set; }

    [MaxLength(120, ErrorMessage = "Contact must be 120 characters or fewer.")]
    [Display(Name = "How people can reach you")]
    public string? Contact { get; set; }

    /// <summary>A connection-intent key, or null/blank for unspecified. Validated against the closed set.</summary>
    [Display(Name = "What you're here for")]
    public string? ConnectionIntent { get; set; }

    /// <summary>
    /// Free-text, comma- or newline-separated interests to show publicly on match cards. Parsed,
    /// sanitized, de-duplicated and capped server-side (see <see cref="Profile.ShowableInterests"/>).
    /// Public and opt-in — separate from the discarded matching fingerprint.
    /// </summary>
    [MaxLength(600, ErrorMessage = "That's a lot of interests — please shorten the list.")]
    [Display(Name = "Interests to show on your card")]
    public string? ShowableInterests { get; set; }
}
