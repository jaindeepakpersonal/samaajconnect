using Sangam.MasterData.Domain.Common;
using Sangam.MasterData.Domain.Lookups.Events;

namespace Sangam.MasterData.Domain.Lookups;

/// <summary>
/// One row in one of the platform's flat, admin-managed reference lists:
/// Country, ProfessionType, Gender, MaritalStatus, or Qualification.
/// </summary>
/// <remarks>
/// These categories share an identical shape - a code, a label, a sort
/// position and an active flag - and nothing else on the platform reasons
/// about them differently from one category to the next. A separate CRUD
/// aggregate for each would be that many copies of the same file with
/// the words changed, which is not the "three similar lines" root CLAUDE.md
/// asks to leave alone; it is the same table, five times over.
///
/// <c>City</c>, <c>Locality</c> and <c>Gautra</c> are deliberately <b>not</b>
/// part of this table. City and Locality carry a parent id (a hierarchy this
/// flat shape has no column for), and Gautra is tenant-scoped - folding a
/// tenant id into this table would mean every read of every global lookup
/// also passes through the tenant query filter, for data that belongs to no
/// tenant at all. Each gets its own aggregate instead.
/// </remarks>
public sealed class LookupItem : AggregateRoot
{
    public Guid Id { get; private set; }
    public LookupCategory Category { get; private set; }
    public string Code { get; private set; } = null!;
    public string Label { get; private set; } = null!;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    private LookupItem() { }   // EF Core

    public static LookupItem Create(
        LookupCategory category, string code, string label, int sortOrder, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var item = new LookupItem
        {
            Id = Guid.NewGuid(),
            Category = category,
            Code = code.Trim(),
            Label = label.Trim(),
            SortOrder = sortOrder,
            IsActive = true,
        };

        item.Raise(new LookupItemCreatedDomainEvent(
            item.Id, item.Category.ToString(), item.Code, item.Label, now));

        return item;
    }

    public void Update(string label, int sortOrder, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        Label = label.Trim();
        SortOrder = sortOrder;

        Raise(new LookupItemUpdatedDomainEvent(Id, Category.ToString(), Code, Label, now));
    }

    /// <summary>
    /// Deactivates, never deletes. Other services hold this row's id as a
    /// plain foreign key across a service boundary with no referential
    /// integrity to enforce it, so a hard delete would leave those references
    /// dangling with nothing to say so.
    /// </summary>
    public bool Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;

        Raise(new LookupItemDeactivatedDomainEvent(Id, Category.ToString(), now));

        return true;
    }
}

/// <summary>The flat, platform-wide reference lists this aggregate holds.</summary>
public enum LookupCategory
{
    Country = 1,
    ProfessionType = 2,
    Gender = 3,
    MaritalStatus = 4,

    /// <summary>
    /// A member's highest educational qualification. Added 2026-09-11 for
    /// My Profile's Educational card - the same flat, admin-managed shape as
    /// the other four, so it needed no new aggregate and no migration:
    /// <see cref="LookupItem.Category"/> is stored as a string.
    /// </summary>
    Qualification = 5,

    /// <summary>
    /// A business's category (Grocery, Textiles, Catering, ...), for the new
    /// Businesses module's directory filter. Added 2026-09-12, the same
    /// zero-migration story as <see cref="Qualification"/> - a Samaaj Admin
    /// seeds actual values through the existing Master Data screen.
    /// </summary>
    BusinessCategory = 6,

    /// <summary>
    /// A Samaaj event's type (Religious, Cultural, Social, ...). Added
    /// 2026-09-12 for events-service's own `EventTypeId`, the same
    /// zero-migration story as every category before it.
    /// </summary>
    EventType = 7,
}
