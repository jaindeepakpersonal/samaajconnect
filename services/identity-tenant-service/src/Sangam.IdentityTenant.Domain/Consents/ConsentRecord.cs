using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Consents;

public enum ConsentAction
{
    Granted = 1,
    Withdrawn = 2,
}

/// <summary>
/// One consent decision, at one moment, for one purpose.
/// </summary>
/// <remarks>
/// Append-only. Granting and withdrawing each write a row and nothing is ever
/// updated in place, because DPDP section 6(7) requires a Data Fiduciary to be
/// able to produce the consent it relied on - which a mutable record cannot do.
/// The current state of a purpose is the latest row for it.
/// </remarks>
public sealed class ConsentRecord : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }

    public ConsentPurpose Purpose { get; private set; }
    public ConsentAction Action { get; private set; }

    /// <summary>Which version of the notice the person was shown.</summary>
    public string NoticeVersion { get; private set; } = null!;

    /// <summary>
    /// How the decision was made - "Registration", "MemberPortal". Recorded
    /// because section 6 requires consent to be unambiguous, and "where did
    /// this come from" is the first question anyone asks about a disputed one.
    /// </summary>
    public string Source { get; private set; } = null!;

    public DateTimeOffset RecordedAt { get; private set; }

    private ConsentRecord() { }

    public static ConsentRecord Grant(
        Guid tenantId, Guid userId, ConsentPurpose purpose, string source, DateTimeOffset now) =>
        Write(tenantId, userId, purpose, ConsentAction.Granted, source, now);

    public static ConsentRecord Withdraw(
        Guid tenantId, Guid userId, ConsentPurpose purpose, string source, DateTimeOffset now) =>
        Write(tenantId, userId, purpose, ConsentAction.Withdrawn, source, now);

    private static ConsentRecord Write(
        Guid tenantId,
        Guid userId,
        ConsentPurpose purpose,
        ConsentAction action,
        string source,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var record = new ConsentRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Purpose = purpose,
            Action = action,
            NoticeVersion = ConsentNotice.CurrentVersion,
            Source = source.Trim(),
            RecordedAt = now,
        };

        // Announced so the audit service records it. A consent decision is
        // exactly the kind of thing that has to be provable later.
        record.Raise(new ConsentRecordedDomainEvent(
            record.Id, tenantId, userId, purpose.ToString(), action.ToString(),
            record.NoticeVersion, now));

        return record;
    }
}
