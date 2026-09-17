using System.ComponentModel.DataAnnotations;

namespace Profiler.Web.ViewModels;

public class LoginViewModel
{
    [Required]
    public string Username { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    /// <summary>A circle invite token carried through from the invite page (see RegisterViewModel).</summary>
    public string? CircleInvite { get; set; }
}
