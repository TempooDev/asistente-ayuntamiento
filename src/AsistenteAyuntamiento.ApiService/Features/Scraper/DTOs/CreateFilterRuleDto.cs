namespace AsistenteAyuntamiento.ApiService.Features.Scraper.DTOs;

using System.ComponentModel.DataAnnotations;

public class CreateFilterRuleDto
{
    [Required] public string Provider { get; set; } = string.Empty;
    [Required] public string FilterType { get; set; } = string.Empty;
    [Required] public string Value { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
