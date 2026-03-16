using Microsoft.AspNetCore.Http;

namespace Profiler.Web.ViewModels;

public class ConnectSourcesViewModel
{
    public string? GitHubUser { get; set; }
    public string? GitHubToken { get; set; }
    public IFormFile? GoodreadsCsv { get; set; }
    public IFormFile? NetflixCsv { get; set; }
    public string? GoogleToken { get; set; }
    public string? FacebookToken { get; set; }
    public string? PinterestToken { get; set; }
}
