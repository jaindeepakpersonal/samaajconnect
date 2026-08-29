using Sangam.Pathshala.Domain.Common;
using Sangam.Pathshala.Domain.Enrolments.Events;

namespace Sangam.Pathshala.Domain.Enrolments;

/// <summary>
/// One child's place at a Pathshala: requested by a parent, placed in a class
/// by the Pathshala.
/// </summary>
/// <remarks>
/// <b>Why enrolment is two steps.</b> The requirement puts the endpoint at
/// <c>POST /pathshalas/{id}/enrollments</c>, not at a class, so somebody at the
/// Pathshala still has to decide which class the child joins. That decision
/// cannot be the parent's - they do not know the rosters - so a request and a
/// placement were always going to be separate acts.
///
/// It also answers a question this service structurally cannot. A parent
/// enrols a child by <c>ChildProfileId</c>, and whether that child is theirs is
/// member-family-service's fact, not ours; asking it synchronously would reach
/// across a service boundary the repo avoids, and mirroring it here would mean
/// a projection that is stale exactly when a family is new. The staff placing
/// the child know the family and are already deciding. So the check that could
/// not be automated is done by the person who was going to look anyway - and an
/// unplaced request grants no access to anything, because nothing has been
/// recorded against it yet.
///
/// <b>Attendance and results are not held here.</b> A child accumulates a
/// year of attendance rows and a term of results; an aggregate that loaded them
/// would read the year to mark one day. Both are separate roots with unique
/// indexes - see <see cref="AttendanceEntry"/> and <see cref="ExamResult"/>.
/// </remarks>
public sealed class StudentEnrolment : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PathshalaId { get; private set; }
    public Guid ChildProfileId { get; private set; }

    /// <summary>The class this child was placed in. Null until they are placed.</summary>
    public Guid? ClassId { get; private set; }

    /// <summary>The session they were placed into, fixed at placement.</summary>
    public Guid? SessionId { get; private set; }

    /// <summary>
    /// The member who asked for the place - in practice the family head.
    /// </summary>
    /// <remarks>
    /// This is the ownership record the read paths are decided against: a
    /// parent sees the enrolment they requested. It is not re-derived from
    /// member-family-service on each read, because a household changing later
    /// should not silently retract someone's view of a request they made.
    /// </remarks>
    public Guid RequestedByMemberId { get; private set; }

    /// <summary>
    /// The child's own account, once they have one. Null while they are a child
    /// profile.
    /// </summary>
    /// <remarks>
    /// Set by consuming <c>identity.child-conversion.completed.v1</c>. A child
    /// profile has no login, so until conversion the only person who can read
    /// these records is the parent who requested the place. After it, the
    /// now-adult student can read their own history under their own account,
    /// which is the whole point of the conversion flow preserving Pathshala
    /// records.
    /// </remarks>
    public Guid? StudentUserId { get; private set; }

    public EnrolmentStatus Status { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? EnrolledAt { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }

    private StudentEnrolment() { }   // EF Core

    public static StudentEnrolment Request(
        Guid tenantId,
        Guid pathshalaId,
        Guid childProfileId,
        Guid requestedByMemberId,
        DateTimeOffset now)
    {
        var enrolment = new StudentEnrolment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PathshalaId = pathshalaId,
            ChildProfileId = childProfileId,
            RequestedByMemberId = requestedByMemberId,
            Status = EnrolmentStatus.Requested,
            RequestedAt = now,
        };

        enrolment.Raise(new EnrolmentRequestedDomainEvent(
            enrolment.Id, tenantId, pathshalaId, childProfileId, requestedByMemberId, now));

        return enrolment;
    }

    /// <summary>
    /// Places the child in a class. Returns false when the request is not
    /// waiting to be placed.
    /// </summary>
    public bool PlaceIn(Guid classId, Guid sessionId, DateTimeOffset now)
    {
        if (Status != EnrolmentStatus.Requested)
        {
            return false;
        }

        ClassId = classId;
        SessionId = sessionId;
        Status = EnrolmentStatus.Active;
        EnrolledAt = now;

        Raise(new StudentEnrolledDomainEvent(
            Id, TenantId, PathshalaId, classId, sessionId, ChildProfileId, now));

        return true;
    }

    /// <summary>Turns a request down. Only a request can be declined.</summary>
    public bool Decline(DateTimeOffset now)
    {
        if (Status != EnrolmentStatus.Requested)
        {
            return false;
        }

        Status = EnrolmentStatus.Declined;
        WithdrawnAt = now;

        return true;
    }

    /// <summary>
    /// Withdraws a placed student. Their attendance and results stay.
    /// </summary>
    /// <remarks>
    /// Withdrawing is not erasure. A child who leaves in March still attended
    /// from June, and a Pathshala asked what its attendance was that year has to
    /// be able to answer. Erasure is a different act, driven by
    /// <c>identity.user.erased.v1</c> and DPDP section 12.
    /// </remarks>
    public bool Withdraw(DateTimeOffset now)
    {
        if (Status != EnrolmentStatus.Active)
        {
            return false;
        }

        Status = EnrolmentStatus.Withdrawn;
        WithdrawnAt = now;

        return true;
    }

    /// <summary>
    /// Links this enrolment to the account the child now holds.
    /// </summary>
    /// <remarks>
    /// Idempotent, because the conversion event is delivered at least once.
    /// </remarks>
    public bool LinkTo(Guid studentUserId)
    {
        if (StudentUserId == studentUserId)
        {
            return false;
        }

        StudentUserId = studentUserId;

        return true;
    }

    /// <summary>Whether attendance and results may be recorded against this.</summary>
    public bool IsOnRoll => Status == EnrolmentStatus.Active;

    /// <summary>
    /// Whether <paramref name="memberId"/> may read this enrolment's records
    /// without being Pathshala staff.
    /// </summary>
    /// <remarks>
    /// The parent who asked for the place, or the student themselves once they
    /// have an account. Staff access is decided separately, against the class
    /// and the Pathshala, because it does not depend on whose child this is.
    /// </remarks>
    public bool BelongsTo(Guid memberId) =>
        RequestedByMemberId == memberId || StudentUserId == memberId;
}

public enum EnrolmentStatus
{
    /// <summary>A parent has asked; nobody has placed the child yet.</summary>
    Requested = 1,

    Active = 2,
    Withdrawn = 3,
    Declined = 4,
}
