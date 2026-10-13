using System.ComponentModel.DataAnnotations;
using AsistenteAyuntamiento.Domain.Common.Enums;

namespace AsistenteAyuntamiento.Domain.Features.Ingestion;

public class DocumentJobState
{
    [Key]
    public string DocumentId { get; set; } = string.Empty;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
}
