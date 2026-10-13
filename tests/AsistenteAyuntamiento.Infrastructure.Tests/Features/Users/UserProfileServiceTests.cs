using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Users;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Shared.Features.Users;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Users;

public class UserProfileServiceTests
{
    private readonly string _databaseName = $"profiles-{Guid.NewGuid()}";

    private AppDbContext Context(string tenant = "org_A") => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_databaseName).Options,
        Mock.Of<ICurrentTenantService>(t => t.TenantId == tenant));

    [Fact]
    public async Task First_access_creates_the_profile_with_the_display_name()
    {
        await using var db = Context();

        var profile = await new UserProfileService(db).GetOrCreateAsync("auth0|1", "org_A", "Ana Pérez");

        profile.FullName.Should().Be("Ana Pérez");
        (await db.UserProfiles.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task The_profile_is_found_by_identity_even_from_another_tenant_context()
    {
        await using (var db = Context("org_A"))
        {
            await new UserProfileService(db).GetOrCreateAsync("auth0|1", "org_A", "Ana");
        }

        await using var other = Context("org_B");
        var profile = await new UserProfileService(other).GetOrCreateAsync("auth0|1", "org_B", "Otro nombre");

        profile.FullName.Should().Be("Ana");
        (await other.UserProfiles.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Update_changes_only_the_editable_fields()
    {
        await using var db = Context();
        var sut = new UserProfileService(db);
        var created = await sut.GetOrCreateAsync("auth0|1", "org_A", "Ana");

        await sut.UpdateAsync("auth0|1", "org_A", null, new UserProfileDto { FullName = "Ana P.", Department = "Urbanismo", Auth0UserId = "auth0|other" });

        await using var check = Context();
        var stored = await check.UserProfiles.SingleAsync();
        stored.Id.Should().Be(created.Id);
        stored.Auth0UserId.Should().Be("auth0|1");
        stored.FullName.Should().Be("Ana P.");
        stored.Department.Should().Be("Urbanismo");
    }
}
