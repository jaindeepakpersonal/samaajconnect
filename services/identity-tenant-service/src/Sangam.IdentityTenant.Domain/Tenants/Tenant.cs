using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Tenants;

/// <summary>
/// A Samaaj (community organisation). Platform-level: this is the row every
/// other entity in the platform points at via its own TenantId, so it is not
/// itself tenant-query-filtered (see <see cref="ITenantScopedEntity"/>).
/// </summary>
public sealed class Tenant : AggregateRoot
{
    private readonly List<string> _enabledModules = [];

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>Subdomain label the gateway resolves to this tenant. Unique platform-wide.</summary>
    public string Slug { get; private set; } = null!;

    /// <summary>Optional custom domain (CNAME) once a Samaaj brings its own.</summary>
    public string? Domain { get; private set; }

    public string? LogoUrl { get; private set; }
    public string? ContactPerson { get; private set; }
    public string? ContactEmail { get; private set; }

    /// <summary>
    /// Who a member complains to about how their data is handled
    /// (DPDP section 13). Kept separate from the general contact above: the
    /// Act requires a published means of grievance redressal specifically, and
    /// conflating it with "who do I ask about the next event" would make it
    /// impossible to tell whether a Samaaj has actually named one.
    /// </summary>
    public string? GrievanceContactName { get; private set; }
    public string? GrievanceContactEmail { get; private set; }
    public string? GrievanceContactPhone { get; private set; }
    public TenantStatus Status { get; private set; }

    /// <summary>
    /// Module keys this Samaaj has switched on. The gateway reads this to 404
    /// routes for disabled modules (ARCHITECTURE.md §6).
    /// </summary>
    public IReadOnlyCollection<string> EnabledModules => _enabledModules.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }

    private Tenant() { }   // EF Core

    public static Tenant Create(
        string name,
        string slug,
        string? domain,
        string? contactPerson,
        string? contactEmail,
        IEnumerable<string>? enabledModules,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = NormalizeSlug(slug),
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain.Trim().ToLowerInvariant(),
            ContactPerson = string.IsNullOrWhiteSpace(contactPerson) ? null : contactPerson.Trim(),
            ContactEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim().ToLowerInvariant(),
            // A new Samaaj starts Inactive: creating the record and letting it
            // serve traffic are two separate, separately audited decisions.
            Status = TenantStatus.Inactive,
            CreatedAt = createdAt,
        };

        if (enabledModules is not null)
        {
            tenant._enabledModules.AddRange(
                enabledModules
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Select(m => m.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        tenant.Raise(new TenantCreatedDomainEvent(
            tenant.Id,
            tenant.Name,
            tenant.Slug,
            tenant.Status.ToString(),
            tenant.CreatedAt));

        return tenant;
    }

    /// <summary>
    /// Names the person a member complains to about their data (DPDP s.13).
    /// Passing nothing clears it, which is a Samaaj saying it has not named
    /// one - visible rather than hidden behind a stale value.
    /// </summary>
    public void SetGrievanceContact(string? name, string? email, string? phone)
    {
        GrievanceContactName = Normalize(name);
        GrievanceContactEmail = Normalize(email)?.ToLowerInvariant();
        GrievanceContactPhone = Normalize(phone);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Moves the Samaaj between Active, Inactive and Archived. Returns false
    /// when the status is already the requested one, so a caller can report
    /// "nothing to do" without an event being published for a non-change.
    /// </summary>
    public bool ChangeStatus(TenantStatus status, DateTimeOffset occurredAt)
    {
        if (Status == status)
        {
            return false;
        }

        var previous = Status;
        Status = status;

        Raise(new TenantStatusChangedDomainEvent(
            Id, previous.ToString(), status.ToString(), occurredAt));

        return true;
    }

    /// <summary>
    /// Lowercases and trims a slug so "  Mumbai-Samaaj " and "mumbai-samaaj"
    /// can never become two different tenants.
    /// </summary>
    public static string NormalizeSlug(string slug) => slug.Trim().ToLowerInvariant();
}
