using System.ComponentModel.DataAnnotations;

namespace Profiler.Web.ViewModels;

/// <summary>
/// Recovery is a single post — username, code, new password — rather than a verify step followed by
/// a reset step, so there is no half-authenticated state to hold between requests.
/// </summary>
public class RecoverAccountViewModel
{
    [Required]
    public string Username { get; set; } = "";

    [Required]
    [Display(Name = "Recovery code")]
    public string Code { get; set; } = "";

    [Required, DataType(DataType.Password), MinLength(8)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    [Display(Name = "Confirm new password")]
    public string ConfirmNewPassword { get; set; } = "";
}
