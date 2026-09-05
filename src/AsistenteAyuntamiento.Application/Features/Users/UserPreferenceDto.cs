namespace AsistenteAyuntamiento.Application.Features.Users;

public class UserPreferenceDto
{
    public List<string> Topics { get; set; } = new();
    public List<string> Locations { get; set; } = new();
}
