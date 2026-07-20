using System.ComponentModel.DataAnnotations;

namespace Profiler.Web.ViewModels;

public class RegisterViewModel
{
    [Required, MinLength(3), MaxLength(50)]
    public string Username { get; set; } = "";

    [Required, DataType(DataType.Password), MinLength(8)]
    public string Password { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = "";
}
