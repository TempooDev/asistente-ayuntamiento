using System.ComponentModel.DataAnnotations;

namespace AsistenteAyuntamiento.Application.Features.Scraper.DTOs;

public class TriggerScrapeDto
{
    [Required] public string Provider { get; set; } = string.Empty;
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public List<string>? Sections { get; set; }
}
