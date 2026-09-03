namespace AsistenteAyuntamiento.ApiService.Features.Scraper;

public class ScraperStateService
{
    public bool IsScraping { get; set; } = false;
    public string ScrapeMessage { get; set; } = string.Empty;
}
