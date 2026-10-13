using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Users;
using AsistenteAyuntamiento.Domain.Features.Users;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;

namespace AsistenteAyuntamiento.Application.Tests.Features.Users;

/// <summary>Preferences are per user and organization; extracted ones are merged into the stored ones.</summary>
public class UserPreferenceServiceTests
{
    private readonly List<UserPreference> _stored = [];
    private readonly Mock<IAppDbContext> _db = new();

    private UserPreferenceService CreateSut(string tenant = "org_A")
    {
        var set = _stored.BuildMockDbSet();
        set.Setup(s => s.Add(It.IsAny<UserPreference>())).Callback<UserPreference>(_stored.Add);
        _db.Setup(d => d.UserPreferences).Returns(set.Object);
        return new UserPreferenceService(_db.Object, Mock.Of<ICurrentTenantService>(t => t.TenantId == tenant));
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

    [Fact]
    public async Task The_same_user_has_separate_preferences_in_each_organization()
    {
        _stored.Add(new UserPreference { Auth0UserId = "auth0|1", TenantId = "org_A", Topics = ["Vivienda"] });

        (await CreateSut("org_B").GetPreferencesAsync("auth0|1")).Topics.Should().BeEmpty();

        await CreateSut("org_B").UpdatePreferencesAsync("auth0|1", new UserPreferenceDto { Topics = ["Empleo"] });

        _stored.Should().HaveCount(2);
        _stored.Single(p => p.TenantId == "org_A").Topics.Should().Equal("Vivienda");
        _stored.Single(p => p.TenantId == "org_B").Topics.Should().Equal("Empleo");
    }

    [Fact]
    public async Task Duplicates_in_the_extracted_values_are_removed()
    {
        await CreateSut().MergePreferencesAsync("auth0|1", new UserPreferenceDto { Topics = ["Vivienda", "vivienda ", " VIVIENDA"], Locations = [] });

        _stored.Single().Topics.Should().Equal("Vivienda");
    }
}
