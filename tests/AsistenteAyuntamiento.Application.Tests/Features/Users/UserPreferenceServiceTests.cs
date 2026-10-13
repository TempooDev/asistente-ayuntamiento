using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Users;
using AsistenteAyuntamiento.Domain.Features.Users;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Users;

/// <summary>Preferences extracted from chat history are merged into the stored ones.</summary>
public class UserPreferenceServiceTests
{
    private readonly List<UserPreference> _stored = [];
    private readonly Mock<IAppDbContext> _db = new();

    private UserPreferenceService CreateSut()
    {
        var set = _stored.BuildMockDbSet();
        set.Setup(s => s.Add(It.IsAny<UserPreference>())).Callback<UserPreference>(_stored.Add);
        _db.Setup(d => d.UserPreferences).Returns(set.Object);
        return new UserPreferenceService(_db.Object, Mock.Of<ICurrentTenantService>(t => t.TenantId == "org_A"));
    }

    [Fact]
    public async Task First_merge_creates_the_preferences_in_the_current_tenant()
    {
        await CreateSut().MergePreferencesAsync("auth0|1", new UserPreferenceDto { Topics = ["Vivienda"], Locations = ["Málaga"] });

        var created = _stored.Should().ContainSingle().Subject;
        created.TenantId.Should().Be("org_A");
        created.Topics.Should().Equal("Vivienda");
        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Only_new_values_are_added_ignoring_case()
    {
        _stored.Add(new UserPreference { Auth0UserId = "auth0|1", TenantId = "org_A", Topics = ["Vivienda"], Locations = ["Málaga"] });

        await CreateSut().MergePreferencesAsync("auth0|1", new UserPreferenceDto { Topics = ["vivienda", "Empleo"], Locations = ["MÁLAGA"] });

        _stored.Single().Topics.Should().Equal("Vivienda", "Empleo");
        _stored.Single().Locations.Should().Equal("Málaga");
    }

    [Fact]
    public async Task Nothing_new_does_not_write()
    {
        _stored.Add(new UserPreference { Auth0UserId = "auth0|1", TenantId = "org_A", Topics = ["Vivienda"] });

        await CreateSut().MergePreferencesAsync("auth0|1", new UserPreferenceDto { Topics = ["VIVIENDA"] });

        _db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
