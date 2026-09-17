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

    /// <summary>
    /// Signed, time-limited proof that this form was actually loaded (not POSTed blind by a script).
    /// Populated by the GET, verified by the POST. See <see cref="Security.RegistrationGuard"/>.
    /// </summary>
    public string? FormTicket { get; set; }

    /// <summary>
    /// Honeypot: a decoy field hidden from humans. A real person leaves it empty; a form-filling bot
    /// fills it and is rejected. Never shown, never stored — its only value is being blank.
    /// </summary>
    public string? Website { get; set; }

    /// <summary>A circle invite token carried through from the invite page, so the new account lands
    /// back on the join confirmation. Validated only by shape here; the join page verifies it.</summary>
    public string? CircleInvite { get; set; }
}
