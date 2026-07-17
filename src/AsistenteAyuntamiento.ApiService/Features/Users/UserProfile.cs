using System.ComponentModel.DataAnnotations;

namespace AsistenteAyuntamiento.ApiService.Features.Users;

public class UserProfile
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string Auth0UserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string TenantId { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? FullName { get; set; }

    [MaxLength(100)]
    public string? Department { get; set; }

    [MaxLength(50)]
    public string? Position { get; set; }

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
