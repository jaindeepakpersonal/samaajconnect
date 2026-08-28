using Sangam.MemberFamily.Domain.Common;

namespace Sangam.MemberFamily.Domain.Children;

/// <summary>
/// A request to turn a child record into a member account of its own.
/// </summary>
/// <remarks>
/// Admin-approved, decided 2026-08-28 (see DEVELOPMENT_PLAN.md open decisions).
/// The family raises the request; a Samaaj admin approves it before any login
/// exists. Self-service was the alternative and was rejected as the less safe
/// default - creating a platform login is not something a household should be
/// able to do unilaterally, and the rule is easy to relax later if it proves
/// too slow in practice.
/// </remarks>
public sealed class ChildConversionRequest : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ChildProfileId { get; private set; }

    /// <summary>The member who raised it - the family head.</summary>
    public Guid RequestedByMemberId { get; private set; }

    /// <summary>
    /// The identifier the new login will use. Collected at request time so the
    /// approving admin can see what account they are authorising, and so
    /// identity-tenant-service has everything it needs from the event alone.
    /// </summary>
    public string MobileOrEmail { get; private set; } = null!;

    public ConversionStatus Status { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionNote { get; private set; }

    private ChildConversionRequest() { }

    public static ChildConversionRequest Raise(
        Guid tenantId,
        Guid childProfileId,
        Guid requestedByMemberId,
        string mobileOrEmail,
        DateTimeOffset requestedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mobileOrEmail);

        return new ChildConversionRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ChildProfileId = childProfileId,
            RequestedByMemberId = requestedByMemberId,
            MobileOrEmail = mobileOrEmail.Trim().ToLowerInvariant(),
            Status = ConversionStatus.Pending,
            RequestedAt = requestedAt,
        };
    }

    /// <summary>
    /// Approves the request and announces it, so identity-tenant-service can
    /// create the login. Returns false when the request was already decided.
    /// </summary>
    public bool Approve(Guid decidedBy, string? note, DateTimeOffset decidedAt, string fullName)
    {
        if (Status != ConversionStatus.Pending)
        {
            return false;
        }

        Status = ConversionStatus.Approved;
        DecidedBy = decidedBy;
        DecidedAt = decidedAt;
        DecisionNote = Trim(note);

        // Carries no secret. The audit service records every event payload
        // verbatim, so a password or a hash travelling here would end up in an
        // append-only log that is deliberately impossible to redact.
        Raise(new ChildConversionApprovedDomainEvent(
            Id, TenantId, ChildProfileId, fullName, MobileOrEmail, decidedBy, decidedAt));

        return true;
    }

    public bool Reject(Guid decidedBy, string? note, DateTimeOffset decidedAt)
    {
        if (Status != ConversionStatus.Pending)
        {
            return false;
        }

        Status = ConversionStatus.Rejected;
        DecidedBy = decidedBy;
        DecidedAt = decidedAt;
        DecisionNote = Trim(note);

        return true;
    }

    private static string? Trim(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}
