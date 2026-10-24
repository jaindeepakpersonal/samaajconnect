namespace Sangam.Businesses.Domain.Businesses;

/// <summary>
/// A product listed under a <see cref="Business"/>. An owned entity, not its
/// own aggregate root - mutated only through methods on <see cref="Business"/>,
/// the same shape volunteer-groups-service's own `GroupApplication`/
/// `GroupMember` are held and mutated only through `VolunteerGroup`.
/// </summary>
public sealed class Product
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    /// <summary>Integer paise on the wire, per libs/shared's money convention.</summary>
    public int PriceInPaise { get; private set; }

    /// <summary>True from creation; the owner toggles rather than deleting, so past orders still name a real product.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>The product's image, a <c>BusinessImage</c> of its business. Null when none.</summary>
    public Guid? PhotoId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    private Product() { }

    internal Product(Guid id, string name, string? description, int priceInPaise, DateTimeOffset now)
    {
        Id = id;
        Name = name;
        Description = description;
        PriceInPaise = priceInPaise;
        IsAvailable = true;
        CreatedAt = now;
    }

    internal void Update(string name, string? description, int priceInPaise)
    {
        Name = name;
        Description = description;
        PriceInPaise = priceInPaise;
    }

    internal void SetAvailability(bool available) => IsAvailable = available;

    /// <summary>Points at an image, or none, and answers the one it replaces.</summary>
    internal Guid? SetPhoto(Guid? photoId)
    {
        var previous = PhotoId;

        PhotoId = photoId;

        return previous;
    }
}
