using System.ComponentModel.DataAnnotations;

namespace AsistenteAyuntamiento.Domain.Features.Users;

public class UserPreference
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string Auth0UserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string TenantId { get; set; } = string.Empty;

    public List<string> Topics { get; set; } = new();

    public List<string> Locations { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
