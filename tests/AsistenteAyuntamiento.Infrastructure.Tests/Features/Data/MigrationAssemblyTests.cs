using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Infrastructure.Data.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Data;

/// <summary>Guards the migration history and embedded SQL scripts against folder or namespace moves.</summary>
public class MigrationAssemblyTests
{
    [Theory]
    [InlineData("AsistenteAyuntamiento.Infrastructure.Data.Migrations.Scripts.Up_AddChildFragmentsTsvTrigger.sql")]
    [InlineData("AsistenteAyuntamiento.Infrastructure.Data.Migrations.Scripts.Down_DropChildFragmentsTsvTrigger.sql")]
    public void Migration_sql_scripts_are_embedded_under_the_name_the_migration_loads(string resourceName)
    {
        typeof(AddRagQuestionArena).Assembly.GetManifestResourceNames().Should().Contain(resourceName);
    }

    [Fact]
    public void Every_migration_is_discovered_in_chronological_order()
    {
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=unused").Options,
            Mock.Of<ICurrentTenantService>());

        var migrations = db.Database.GetMigrations().ToList();

        migrations.Should().HaveCount(23);
        migrations.Should().BeInAscendingOrder(StringComparer.Ordinal);
        migrations.Should().Contain("20260904162245_AddRagQuestionArena");
    }
}
