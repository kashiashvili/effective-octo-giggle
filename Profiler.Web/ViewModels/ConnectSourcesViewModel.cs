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
    public string? SpotifyToken { get; set; }
    public string? TwitterToken { get; set; }
    public string? LinkedInToken { get; set; }
    public string? RedditToken { get; set; }
    public string? LastFmApiKey { get; set; }
    public string? LastFmUsername { get; set; }
    public string? SteamApiKey { get; set; }
    public string? SteamId { get; set; }
}
