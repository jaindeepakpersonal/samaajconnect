using Sangam.Boli.Domain.Auctions.Events;
using Sangam.Boli.Domain.Common;

namespace Sangam.Boli.Domain.Auctions;

/// <summary>
/// An occasion a Samaaj holds Boli at — a Paryushan, a temple anniversary, a
/// fundraiser — and the types of Boli it offers.
/// </summary>
/// <remarks>
/// <b>The Boli themselves are not part of this aggregate.</b> A Boli is loaded,
/// locked and written on its own, once per bid, on the most contended write path
/// this service has; pulling an occasion and every Boli under it into memory to
/// accept one bid would be the same mistake celebrity-voting-service documents
/// about votes, one size up.
///
/// What is here is what is small and bounded: the occasion, and the handful of
/// Boli types it defines. A type is a label a Samaaj reuses — "Mangal Deep",
/// "Swapna", "Aarti" — not a thing anybody bids on.
/// </remarks>
public sealed class BoliOccasion : AggregateRoot, ITenantScopedEntity
{
    private readonly List<BoliType> _types = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }

    /// <summary>The day the occasion is held, which is not when bidding runs.</summary>
    public DateOnly OccasionDate { get; private set; }

    public OccasionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<BoliType> Types => _types.AsReadOnly();

    private BoliOccasion() { }   // EF Core

    public static BoliOccasion Create(
        Guid tenantId,
        string title,
        string? description,
        DateOnly occasionDate,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new BoliOccasion
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            OccasionDate = occasionDate,
            Status = OccasionStatus.Upcoming,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Defines a type of Boli for this occasion.
    /// </summary>
    /// <remarks>
    /// Names are unique within an occasion, case-insensitively. Two types called
    /// "Mangal Deep" would leave every list and every published result ambiguous
    /// about which one a Boli belonged to, and the person reading the result is
    /// the one least able to tell them apart.
    /// </remarks>
    public BoliType? DefineType(string name, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmed = name.Trim();

        if (_types.Any(type => string.Equals(type.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var type = BoliType.Create(Id, TenantId, trimmed, description);

        _types.Add(type);

        return type;
    }

    public BoliType? FindType(Guid typeId) => _types.FirstOrDefault(type => type.Id == typeId);

    /// <summary>
    /// Moves the occasion along. Returns false when the move is not one this
    /// occasion can make, rather than throwing — a caller asking to activate an
    /// already-active occasion has not done anything wrong.
    /// </summary>
    public bool MoveTo(OccasionStatus status, DateTimeOffset now)
    {
        if (status == Status)
        {
            return true;
        }

        var allowed = (Status, status) switch
        {
            (OccasionStatus.Upcoming, OccasionStatus.Active) => true,
            (OccasionStatus.Upcoming, OccasionStatus.Closed) => true,
            (OccasionStatus.Active, OccasionStatus.Closed) => true,
            _ => false,
        };

        if (!allowed)
        {
            return false;
        }

        Status = status;

        if (status == OccasionStatus.Closed)
        {
            Raise(new OccasionClosedDomainEvent(Id, TenantId, now));
        }

        return true;
    }
}

/// <summary>A label a Samaaj reuses across occasions. Nobody bids on a type.</summary>
public sealed class BoliType : ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid OccasionId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    private BoliType() { }   // EF Core

    internal static BoliType Create(Guid occasionId, Guid tenantId, string name, string? description) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OccasionId = occasionId,
            Name = name,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        };
}

public enum OccasionStatus
{
    /// <summary>Announced; no Boli under it is taking bids yet.</summary>
    Upcoming = 1,

    Active = 2,
    Closed = 3,
}
