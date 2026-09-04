using Sangam.MemberFamily.Domain.Common;

namespace Sangam.MemberFamily.Domain.Children;

/// <summary>
/// A parent withdrew the consent a child's record was held on (DPDP s.6(4)).
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries ids and a time, and nothing a person could be recognised
/// by.</b> No name, no date of birth, no photograph reference.
/// audit-notification-service subscribes by a catch-all pattern and stores
/// every payload verbatim in an append-only table, so an event announcing that
/// a child's data may no longer be held would otherwise be the one copy of it
/// that survives - permanently, in the service designed to be hard to redact.
/// </para>
/// <para>
/// It is published even though no service reacts to it today, and that is a
/// deliberate exception to this repository's rule about endpoints with no
/// caller. The audit trail <i>is</i> the consumer: a Fiduciary has to be able to
/// show when a consent stopped standing, and the append-only log is where that
/// answer lives. Anything that later needs to react - pathshala-service dropping
/// an enrolment is the obvious one, and is an open question rather than a
/// decision - has a topic to subscribe to rather than a reason to add one.
/// </para>
/// </remarks>
public sealed record ParentalConsentWithdrawnDomainEvent(
    Guid ChildProfileId,
    Guid TenantId,
    Guid FamilyId,
    Guid WithdrawnByMemberId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "members.child.consent-withdrawn.v1";
}
