using AsistenteAyuntamiento.Domain.Features.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AsistenteAyuntamiento.Infrastructure.Data.Configurations;

public sealed class DocumentJobStateConfiguration : IEntityTypeConfiguration<DocumentJobState>
{
    public void Configure(EntityTypeBuilder<DocumentJobState> builder)
    {
        builder.ToTable("DocumentJobStates", "ingestion");
    }
}
