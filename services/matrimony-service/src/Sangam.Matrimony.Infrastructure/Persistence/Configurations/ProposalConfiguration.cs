using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Matrimony.Domain.Proposals;

namespace Sangam.Matrimony.Infrastructure.Persistence.Configurations;

public sealed class ProposalConfiguration : IEntityTypeConfiguration<Proposal>
{
    public void Configure(EntityTypeBuilder<Proposal> builder)
    {
        builder.ToTable("proposals");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.FromMemberId).IsRequired();
        builder.Property(p => p.ToMemberId).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Message).HasMaxLength(500);
        builder.Property(p => p.SentAt).IsRequired();

        // The duplicate-pending check's common case: is there already a
        // live proposal between these two members, in either direction.
        builder.HasIndex(p => new { p.TenantId, p.FromMemberId, p.ToMemberId, p.Status });
        builder.HasIndex(p => new { p.TenantId, p.ToMemberId, p.FromMemberId, p.Status });

        builder.Ignore(p => p.DomainEvents);
    }
}
