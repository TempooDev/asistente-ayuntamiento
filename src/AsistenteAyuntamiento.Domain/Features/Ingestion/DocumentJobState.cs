using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AsistenteAyuntamiento.Domain.Features.Ingestion;

[Table("DocumentJobStates", Schema = "public")]
public class DocumentJobState
{
    [Key]
    public string DocumentId { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
}
