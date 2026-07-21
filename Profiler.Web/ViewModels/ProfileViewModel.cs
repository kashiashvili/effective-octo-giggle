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
}
