using System.ComponentModel.DataAnnotations;

namespace Profiler.Web.ViewModels;

public class RegisterViewModel
{
    private string _username = "";

    /// <summary>
    /// Trimmed on the way in, because the controller trims before storing: validated untrimmed,
    /// "  a  " would satisfy the three-character minimum and then be saved as the one-character
    /// name "a".
    /// </summary>
    [Required, MinLength(3), MaxLength(50)]
    public string Username
    {
        get => _username;
        set => _username = value?.Trim() ?? "";
    }

    [Required, DataType(DataType.Password), MinLength(8)]
    public string Password { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = "";
}
