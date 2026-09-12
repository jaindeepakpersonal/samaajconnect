using Sangam.Businesses.Domain.Businesses.Events;
using Sangam.Businesses.Domain.Common;

namespace Sangam.Businesses.Domain.Businesses;

/// <summary>
/// A business a member owns and lists to their own Samaaj. Unlike Matrimony's
/// `BiodataProfile`, <see cref="Id"/> is a fresh id, not the owner's own -
/// a member may own more than one business, so there is no "one row per
/// member" identity to reuse.
/// </summary>
public sealed class Business : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid OwnerMemberId { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>master-data-service `LookupItem`, `Category = BusinessCategory`.</summary>
    public Guid CategoryId { get; private set; }

    public Guid? CityId { get; private set; }

    public Guid? LocalityId { get; private set; }

    public string? Description { get; private set; }

    /// <summary>
    /// A contact the owner types in for this listing - deliberately not a
    /// read of the member's own privacy-gated Mobile/Email. Publishing a
    /// business is a different act from that member's own base-profile
    /// privacy choices, and reading those here would let a business write
    /// leak a value the member chose to keep private elsewhere.
    /// </summary>
    public string? ContactPhone { get; private set; }

    public string? ContactEmail { get; private set; }

    /// <summary>True from creation; the owner can deactivate/reactivate. Gates directory visibility.</summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    private readonly List<Product> _products = [];

    public IReadOnlyList<Product> Products => _products.AsReadOnly();

    private Business() { }

    public static Business Create(Guid tenantId, Guid ownerMemberId, BusinessFields fields, DateTimeOffset now)
    {
        var business = new Business
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerMemberId = ownerMemberId,
            IsActive = true,
            CreatedAt = now,
        };

        business.ApplyFields(fields);
        business.Raise(new BusinessCreatedDomainEvent(business.Id, tenantId, ownerMemberId, now));

        return business;
    }

    public void Update(BusinessFields fields, DateTimeOffset now)
    {
        ApplyFields(fields);
        UpdatedAt = now;
    }

    /// <summary>Deactivate/reactivate. Reversible and says so - unlike a Matrimony withdrawal, there is no consent to take back here, just a listing to hide.</summary>
    public void SetActive(bool active, DateTimeOffset now)
    {
        if (IsActive == active)
        {
            return;
        }

        IsActive = active;
        UpdatedAt = now;
        Raise(new BusinessStatusChangedDomainEvent(Id, TenantId, OwnerMemberId, active, now));
    }

    public Product AddProduct(string name, string? description, int priceInPaise, DateTimeOffset now)
    {
        var product = new Product(Guid.NewGuid(), name.Trim(), Normalize(description), priceInPaise, now);

        _products.Add(product);
        UpdatedAt = now;

        return product;
    }

    /// <summary>False when no such product exists on this business - the handler turns that into a 404.</summary>
    public bool UpdateProduct(Guid productId, string name, string? description, int priceInPaise, DateTimeOffset now)
    {
        var product = _products.FirstOrDefault(p => p.Id == productId);

        if (product is null)
        {
            return false;
        }

        product.Update(name.Trim(), Normalize(description), priceInPaise);
        UpdatedAt = now;

        return true;
    }

    public bool SetProductAvailability(Guid productId, bool available, DateTimeOffset now)
    {
        var product = _products.FirstOrDefault(p => p.Id == productId);

        if (product is null)
        {
            return false;
        }

        product.SetAvailability(available);
        UpdatedAt = now;

        return true;
    }

    public Product? FindProduct(Guid productId) => _products.FirstOrDefault(p => p.Id == productId);

    private void ApplyFields(BusinessFields fields)
    {
        Name = fields.Name.Trim();
        CategoryId = fields.CategoryId;
        CityId = fields.CityId;
        LocalityId = fields.LocalityId;
        Description = Normalize(fields.Description);
        ContactPhone = Normalize(fields.ContactPhone);
        ContactEmail = Normalize(fields.ContactEmail);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Every field a member submits when creating or editing a business.</summary>
public sealed record BusinessFields(
    string Name,
    Guid CategoryId,
    Guid? CityId,
    Guid? LocalityId,
    string? Description,
    string? ContactPhone,
    string? ContactEmail);
