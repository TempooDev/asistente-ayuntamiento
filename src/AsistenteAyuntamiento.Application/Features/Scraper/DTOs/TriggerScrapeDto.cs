namespace AsistenteAyuntamiento.Application.Features.Scraper.DTOs;

using System.ComponentModel.DataAnnotations;

public class TriggerScrapeDto
{
    [Required] public string Provider { get; set; } = string.Empty;
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public List<string>? Sections { get; set; }
}
