namespace AsistenteAyuntamiento.Shared.Features.Users;

public class UserProfileDto
{
    public Guid Id { get; set; }
    public string Auth0UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Department { get; set; }
    public string? Position { get; set; }
    public string? PhoneNumber { get; set; }
}
