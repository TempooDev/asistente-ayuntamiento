namespace AsistenteAyuntamiento.ApiService.Features.Scraper;

public class ScraperFilterRule
{
    public int Id { get; set; }
    
    /// <summary>
    /// The provider this rule applies to, e.g., "BOE", "BOJA", "BOPMA".
    /// </summary>
    public string Provider { get; set; } = string.Empty;
    
    /// <summary>
    /// The type of filter, e.g., "Department", "Section", "Keyword".
    /// </summary>
    public string FilterType { get; set; } = string.Empty;
    
    /// <summary>
    /// The value to filter by, e.g., "Ministerio de Hacienda".
    /// </summary>
    public string Value { get; set; } = string.Empty;
    
    public bool IsActive { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
