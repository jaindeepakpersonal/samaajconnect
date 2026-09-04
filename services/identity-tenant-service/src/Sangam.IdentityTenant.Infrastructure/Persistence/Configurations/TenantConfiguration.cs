using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Serialized explicitly rather than by enabling Npgsql's dynamic JSON: the
    // opt-in is global and reflection-based, and this is the only POCO in the
    // service that needs to land in a jsonb column.
    private static readonly ValueConverter<List<string>, string> EnabledModulesConverter = new(
        modules => JsonSerializer.Serialize(modules, JsonOptions),
        json => JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>());

    private static readonly ValueComparer<List<string>> EnabledModulesComparer = new(
        (left, right) => left != null && right != null && left.SequenceEqual(right),
        modules => modules.Aggregate(0, (hash, module) => HashCode.Combine(hash, module.GetHashCode())),
        modules => modules.ToList());

    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Slug).HasMaxLength(63).IsRequired();
        builder.Property(t => t.Domain).HasMaxLength(253);
        // No max length: LogoImageId is a Guid pointing at tenant_logos.
        builder.Property(t => t.ContactPerson).HasMaxLength(200);
        builder.Property(t => t.ContactEmail).HasMaxLength(320);
        builder.Property(t => t.GrievanceContactName).HasMaxLength(200);
        builder.Property(t => t.GrievanceContactEmail).HasMaxLength(320);
        builder.Property(t => t.GrievanceContactPhone).HasMaxLength(20);
        builder.Property(t => t.CreatedAt).IsRequired();

        builder.Property(t => t.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // The unique index - not the handler's pre-check - is what actually
        // prevents two Samaaj from claiming one subdomain under concurrency.
        builder.HasIndex(t => t.Slug).IsUnique();

        builder.HasIndex(t => t.Domain)
            .IsUnique()
            .HasFilter("domain IS NOT NULL");

        builder.Property<List<string>>("_enabledModules")
            .HasColumnName("enabled_modules")
            .HasColumnType("jsonb")
            .HasConversion(EnabledModulesConverter, EnabledModulesComparer)
            .HasField("_enabledModules")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();

        builder.Ignore(t => t.EnabledModules);
        builder.Ignore(t => t.DomainEvents);
    }
}
