using System.Text.Json;

namespace Sangam.AuditNotification.Application.IntegrationEvents;

/// <summary>How one topic should be recorded, and whether it deserves a notification.</summary>
/// <param name="BeforeProperties">
/// Payload properties describing the state *before* this event, recorded into
/// AuditLog.BeforeState. SECURITY-CHECKLIST.md asks for before/after on
/// corrections and status changes rather than only on creations: "it changed"
/// is not an audit trail if nobody can tell what it changed from.
///
/// Named properties rather than the whole payload, because the payload is
/// stored verbatim in an append-only table. A status or a set of module keys
/// is safe to keep forever; a member's previous mobile number is not, and
/// copying one here would put personal data somewhere deliberately hard to
/// redact. Where the before-state is personal, the event carries the names of
/// the fields that changed instead of their values - see
/// members.profile.updated.v1.
/// </param>
public sealed record EventDescriptor(
    string Action,
    string EntityName,
    string? EntityIdProperty,
    string? ActorIdProperty = null,
    Func<JsonElement, NotificationSpec?>? Notification = null,
    IReadOnlyList<string>? BeforeProperties = null);

/// <param name="Destination">
/// A contact address to also send this message to, when the event happens to
/// carry one. Null means in-app only, which is the case for almost every event:
/// most of them identify a member by id and nothing else, deliberately, because
/// a payload with a mobile number in it is a payload that later has to be
/// redacted.
///
/// The channel is worked out from the address by
/// <see cref="Sangam.AuditNotification.Domain.Notifications.ContactAddress"/>,
/// since the platform stores one MobileOrEmail per login rather than separate
/// fields. An address it cannot classify raises no outbound copy.
/// </param>
public sealed record NotificationSpec(
    Guid? RecipientUserId,
    string Title,
    string Body,
    string? Destination = null);

/// <summary>
/// The topics this service understands specifically.
/// </summary>
/// <remarks>
/// An unrecognised topic is still audited, with an action derived from the
/// topic name - see <see cref="Describe"/>. Dropping an event because no one
/// has taught this service about it yet would put a hole in the audit trail,
/// which is the one thing an audit trail may not have.
/// </remarks>
public static class KnownEvents
{
    private static readonly Dictionary<string, EventDescriptor> Descriptors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["identity.tenant.created.v1"] = new(
            Action: "TenantCreated",
            EntityName: "Tenant",
            EntityIdProperty: "tenantId"),

        ["identity.tenant.status-changed.v1"] = new(
            Action: "TenantStatusChanged",
            EntityName: "Tenant",
            EntityIdProperty: "tenantId",
            BeforeProperties: ["previousStatus"]),

        // The account-level counterpart, added the same day as the command
        // that raises it. changedByUserId is the actor rather than userId
        // itself, unlike identity.user.logged-in.v1 - suspending somebody is
        // an administrative act done *to* an account, not something the
        // account did to itself, and the derived default leaves the actor
        // blank for precisely the event where "who did this?" matters most.
        ["identity.user.status-changed.v1"] = new(
            Action: "UserStatusChanged",
            EntityName: "User",
            EntityIdProperty: "userId",
            ActorIdProperty: "changedByUserId",
            BeforeProperties: ["previousStatus"]),

        ["identity.user.registered.v1"] = new(
            Action: "UserRegistered",
            EntityName: "User",
            EntityIdProperty: "userId",
            ActorIdProperty: "userId",
            Notification: payload =>
            {
                if (!payload.TryGetProperty("userId", out var userId)
                    || !userId.TryGetGuid(out var recipientId))
                {
                    return null;
                }

                var name = payload.TryGetProperty("fullName", out var fullName)
                    ? fullName.GetString()
                    : null;

                return new NotificationSpec(
                    recipientId,
                    "Welcome to your Samaaj",
                    string.IsNullOrWhiteSpace(name)
                        ? "Your membership is active. Complete your profile to appear in the member directory."
                        : $"Welcome, {name}. Complete your profile to appear in the member directory.",
                    // The identifier the member registered with, so the welcome
                    // also reaches them rather than only waiting in the portal
                    // for a first sign-in that may never come.
                    //
                    // This is not contact verification. Nothing here checks that
                    // the message arrived, or that whoever reads it is the person
                    // who registered - User.IsContactVerified stays false until
                    // something does. See the service CLAUDE.md.
                    Destination: payload.TryGetProperty("mobileOrEmail", out var contact)
                        ? contact.GetString()
                        : null);
            }),

        ["identity.user.logged-in.v1"] = new(
            Action: "UserLoggedIn",
            EntityName: "User",
            EntityIdProperty: "userId",
            ActorIdProperty: "userId"),

        // A self-action, the same shape as logging in. Unlike every other
        // event here, this one's Notification carries a secret rather than a
        // description of one - there is no admin standing between minting
        // this code and delivering it the way there is for an activation
        // code, so the plaintext travelling through this pipeline is the only
        // way it ever reaches the member at all. Destination is set, because
        // an in-app notification is invisible to someone who is, by
        // definition, not signed in yet.
        ["identity.login-otp.requested.v1"] = new(
            Action: "LoginOtpRequested",
            EntityName: "User",
            EntityIdProperty: "userId",
            ActorIdProperty: "userId",
            Notification: payload =>
            {
                if (!payload.TryGetProperty("userId", out var userId)
                    || !userId.TryGetGuid(out var recipientId)
                    || !payload.TryGetProperty("code", out var code))
                {
                    return null;
                }

                return new NotificationSpec(
                    recipientId,
                    "Your sign-in code",
                    $"Your one-time code is {code.GetString()}. It expires in 10 minutes.",
                    Destination: payload.TryGetProperty("mobileOrEmail", out var contact)
                        ? contact.GetString()
                        : null);
            }),

        // A self-action, the same shape as logging in - nobody else can change
        // your password for you, so the actor is always the subject. The
        // in-app notification is what lets a member notice a change they did
        // not make: every *other* session ends the moment this happens
        // (SessionEndReason.PasswordChanged), but the one that made the
        // request is still live for its own token's remaining life, and would
        // otherwise have no way to learn anything happened at all.
        ["identity.user.password-changed.v1"] = new(
            Action: "PasswordChanged",
            EntityName: "User",
            EntityIdProperty: "userId",
            ActorIdProperty: "userId",
            Notification: payload =>
            {
                if (!payload.TryGetProperty("userId", out var userId)
                    || !userId.TryGetGuid(out var recipientId))
                {
                    return null;
                }

                return new NotificationSpec(
                    recipientId,
                    "Your password was changed",
                    "Your password was changed just now. If this wasn't you, contact your "
                    + "Samaaj administrator.");
            }),

        // The entity is the session, not the account, because the same account
        // can have several live sessions and this is about one ending early -
        // "ReuseDetected" on one device says nothing about the others. ActorId
        // is the account itself in both reasons this reaches: a replayed token
        // is discovered on the account's own next refresh attempt, and
        // "EndedByAdministrator" fires the same way when that attempt finds the
        // account suspended, not from a separate admin action with its own
        // actor to record.
        //
        // Until 2026-09-04 this event was declared and documented at length -
        // "the closest thing this platform has to an intrusion signal" - and
        // never once raised. Revoking a session went straight to a raw
        // DbContext with nothing tracked for the Outbox to drain, so the
        // signal reached ILogger.LogWarning and nothing else: not this audit
        // trail, searchable by nobody.
        ["identity.session.revoked.v1"] = new(
            Action: "SessionRevoked",
            EntityName: "Session",
            EntityIdProperty: "sessionId",
            ActorIdProperty: "userId"),

        // The administrative events below all name someone *other* than the
        // subject as the actor, which is exactly why they are described here
        // rather than left to the derived defaults. "Who granted this?" is the
        // first question asked when an account turns out to have been able to
        // do something it should not have, and a derived descriptor answers it
        // with a blank.
        ["identity.admin.invited.v1"] = new(
            Action: "AdminInvited",
            EntityName: "User",
            EntityIdProperty: "userId",
            ActorIdProperty: "invitedBy"),

        ["identity.user.role-granted.v1"] = new(
            Action: "RoleGranted",
            EntityName: "User",
            EntityIdProperty: "userId",
            ActorIdProperty: "grantedBy"),

        ["identity.user.role-revoked.v1"] = new(
            Action: "RoleRevoked",
            EntityName: "User",
            EntityIdProperty: "userId",
            ActorIdProperty: "revokedBy"),

        // A correction to a member's own details. The before-state is the list
        // of field names that changed, never their values: those are personal
        // data, and this table is append-only.
        ["members.profile.updated.v1"] = new(
            Action: "MemberProfileUpdated",
            EntityName: "MemberProfile",
            EntityIdProperty: "memberId",
            ActorIdProperty: "updatedBy",
            BeforeProperties: ["changedFields"]),

        ["identity.tenant.modules-changed.v1"] = new(
            Action: "TenantModulesChanged",
            EntityName: "Tenant",
            EntityIdProperty: "tenantId",
            BeforeProperties: ["previousModules"]),

        // A complete copy of somebody's data left the platform. The event
        // carries ids and a timestamp only - recording what was in the export
        // would make the record of the copy a second copy.
        ["identity.member-data.exported.v1"] = new(
            Action: "MemberDataExported",
            EntityName: "User",
            EntityIdProperty: "userId",
            ActorIdProperty: "userId"),

        // The one topic this service both publishes and consumes. Described
        // here rather than left to the derived defaults for the usual reason:
        // the derived descriptor would record a message sent to an entire
        // Samaaj with no actor on it, and "who told everyone this?" is the only
        // question an audit row about a broadcast exists to answer.
        ["notifications.broadcast.sent.v1"] = new(
            Action: "BroadcastSent",
            EntityName: "Notification",
            EntityIdProperty: "notificationId",
            ActorIdProperty: "sentBy"),

        // volunteer-groups-service's own domain events named who acted in their
        // doc comments - GroupApplicationDecidedDomainEvent: "who let them in?
        // is the first question asked when a group turns out to contain
        // somebody it should not." GroupMemberRemovedDomainEvent: "who removed
        // them, for the same reason." Neither had a descriptor, so both
        // promises were kept by the domain event and broken by the audit row -
        // the derived default left every one of these seven topics with no
        // entity id and, for the three that carry a distinct actor, no actor
        // either. `president.changed.v1` and `role-position.assigned.v1`
        // additionally derive a nonsense entity name from their topic's second
        // segment ("President", "RolePosition") with nothing named "Group" to
        // read; these give the readable one.
        ["volunteer-groups.group.created.v1"] = new(
            Action: "GroupCreated",
            EntityName: "Group",
            EntityIdProperty: "groupId"),

        ["volunteer-groups.application.submitted.v1"] = new(
            Action: "ApplicationSubmitted",
            EntityName: "Application",
            EntityIdProperty: "applicationId",
            ActorIdProperty: "memberId"),

        ["volunteer-groups.application.decided.v1"] = new(
            Action: "ApplicationDecided",
            EntityName: "Application",
            EntityIdProperty: "applicationId",
            ActorIdProperty: "decidedBy"),

        ["volunteer-groups.role-position.assigned.v1"] = new(
            Action: "RolePositionAssigned",
            EntityName: "GroupMember",
            EntityIdProperty: "memberId"),

        ["volunteer-groups.member.removed.v1"] = new(
            Action: "MemberRemoved",
            EntityName: "GroupMember",
            EntityIdProperty: "memberId",
            ActorIdProperty: "removedBy"),

        ["volunteer-groups.president.changed.v1"] = new(
            Action: "PresidentChanged",
            EntityName: "Group",
            EntityIdProperty: "groupId",
            BeforeProperties: ["previousPresidentMemberId"]),

        ["volunteer-groups.group.status-changed.v1"] = new(
            Action: "GroupStatusChanged",
            EntityName: "Group",
            EntityIdProperty: "groupId",
            BeforeProperties: ["previousStatus"]),

        // social-issues-service's IssueStatusChangedDomainEvent already
        // carries a distinct ActorUserId - separate from SubmittedByMemberId,
        // its own doc comment explains why ("the author who is waiting on the
        // answer and the reviewer who gave it") - so the derived default was
        // not merely blank here, it was throwing away a field the event went
        // out of its way to carry.
        ["social-issues.issue.submitted.v1"] = new(
            Action: "IssueSubmitted",
            EntityName: "Issue",
            EntityIdProperty: "issueId",
            ActorIdProperty: "submittedByMemberId"),

        ["social-issues.issue.status-changed.v1"] = new(
            Action: "IssueStatusChanged",
            EntityName: "Issue",
            EntityIdProperty: "issueId",
            ActorIdProperty: "actorUserId",
            BeforeProperties: ["previousStatus"]),

        ["social-issues.issue.published.v1"] = new(
            Action: "IssuePublished",
            EntityName: "Issue",
            EntityIdProperty: "issueId"),

        // timeline-service's third topic in this same shape: PostModeratedDomainEvent
        // carries a distinct ActorUserId separate from AuthorMemberId, for the
        // same reason IssueStatusChangedDomainEvent does - a moderator's
        // decision about somebody else's post is not that member's own act.
        // PostReportedDomainEvent is the opposite case done correctly: its own
        // doc comment says a reporter who could be identified is a reporter who
        // stays quiet, so it carries no reporter id to describe at all - the
        // descriptor below must never grow an ActorIdProperty for it.
        ["timeline.post.submitted.v1"] = new(
            Action: "PostSubmitted",
            EntityName: "Post",
            EntityIdProperty: "postId",
            ActorIdProperty: "authorMemberId"),

        ["timeline.post.moderated.v1"] = new(
            Action: "PostModerated",
            EntityName: "Post",
            EntityIdProperty: "postId",
            ActorIdProperty: "actorUserId",
            BeforeProperties: ["previousStatus"]),

        ["timeline.post.reported.v1"] = new(
            Action: "PostReported",
            EntityName: "Post",
            EntityIdProperty: "postId"),

        // member-family-service's three topics, the fourth service in this
        // pass. ParentalConsentWithdrawnDomainEvent's own doc comment says the
        // append-only audit trail "is the consumer" for this event - "a
        // Fiduciary has to be able to show when a consent stopped standing" -
        // which the derived default could not do at all: both the child the
        // consent was about and the parent who withdrew it were blank.
        ["members.family.created.v1"] = new(
            Action: "FamilyCreated",
            EntityName: "Family",
            EntityIdProperty: "familyId",
            ActorIdProperty: "familyHeadMemberId"),

        ["members.child-conversion.approved.v1"] = new(
            Action: "ChildConversionApproved",
            EntityName: "ChildProfile",
            EntityIdProperty: "childProfileId",
            ActorIdProperty: "approvedBy"),

        ["members.child.consent-withdrawn.v1"] = new(
            Action: "ParentalConsentWithdrawn",
            EntityName: "ChildProfile",
            EntityIdProperty: "childProfileId",
            ActorIdProperty: "withdrawnByMemberId"),

        // identity-tenant-service's own three remaining topics. RoleMatrixChangedDomainEvent's
        // doc comment is the strongest promise found in this whole pass: "the
        // weightiest change an administrator can make on this platform... it
        // is recorded with who made it and what it was before" - a claim the
        // derived default broke completely, on every axis it names.
        ["identity.role-matrix.changed.v1"] = new(
            Action: "RoleMatrixChanged",
            EntityName: "Role",
            EntityIdProperty: "roleId",
            ActorIdProperty: "changedBy",
            BeforeProperties: ["previouslyGranted"]),

        ["identity.consent.recorded.v1"] = new(
            Action: "ConsentRecorded",
            EntityName: "ConsentRecord",
            EntityIdProperty: "consentRecordId",
            ActorIdProperty: "userId"),

        // The admin who approved the conversion is already named on
        // members.child-conversion.approved.v1; this topic only closes the
        // loop once the account exists, with nobody new acting.
        ["identity.child-conversion.completed.v1"] = new(
            Action: "ChildConversionCompleted",
            EntityName: "User",
            EntityIdProperty: "userId"),

        // The remaining eighteen topics on the platform, finishing the sweep
        // this pass started five entries ago. None of these has the "distinct
        // actor differs from subject" shape the earlier finds did - boli,
        // celebrity-voting and events publish system/timing facts with no
        // person to blame or credit, and most of pathshala's are the same.
        // Giving them an entity id is still a real improvement over the
        // derived default's blank: `GET /v1/audit/logs` can now be searched
        // by entity for every topic on the platform, not just the ones with
        // a story.
        ["boli.occasion.closed.v1"] = new(
            Action: "OccasionClosed",
            EntityName: "Occasion",
            EntityIdProperty: "occasionId"),

        ["boli.closed.v1"] = new(
            Action: "BoliClosed",
            EntityName: "Boli",
            EntityIdProperty: "boliId"),

        // WinningMemberId names who won, not who acted - the Samaaj Admin who
        // announced it is not on this event at all - so there is no actor to
        // carry here, the same reasoning events.waitlist.promoted.v1 below
        // gets right for the member it promotes rather than acts on.
        ["boli.result.published.v1"] = new(
            Action: "BoliResultPublished",
            EntityName: "Boli",
            EntityIdProperty: "boliId"),

        ["boli.extended.v1"] = new(
            Action: "BoliExtended",
            EntityName: "Boli",
            EntityIdProperty: "boliId",
            BeforeProperties: ["previousEndAt"]),

        ["celebrity-voting.campaign.status-changed.v1"] = new(
            Action: "CampaignStatusChanged",
            EntityName: "Campaign",
            EntityIdProperty: "campaignId",
            BeforeProperties: ["previousStatus"]),

        ["celebrity-voting.campaign.closed.v1"] = new(
            Action: "CampaignClosed",
            EntityName: "Campaign",
            EntityIdProperty: "campaignId"),

        ["celebrity-voting.results.published.v1"] = new(
            Action: "ResultsPublished",
            EntityName: "Campaign",
            EntityIdProperty: "campaignId"),

        ["events.event.published.v1"] = new(
            Action: "EventPublished",
            EntityName: "Event",
            EntityIdProperty: "eventId"),

        // The one self-action in this batch: a member registers themselves.
        ["events.registration.created.v1"] = new(
            Action: "RegistrationCreated",
            EntityName: "Event",
            EntityIdProperty: "eventId",
            ActorIdProperty: "memberId"),

        ["events.capacity.reached.v1"] = new(
            Action: "CapacityReached",
            EntityName: "Event",
            EntityIdProperty: "eventId"),

        // MemberId here names who benefited from a place opening up, not who
        // acted - a waitlisted member did nothing to be promoted, somebody
        // else gave their place up - so this is not an actor, the same
        // reasoning boli.result.published.v1's WinningMemberId gets above.
        ["events.waitlist.promoted.v1"] = new(
            Action: "WaitlistPromoted",
            EntityName: "Event",
            EntityIdProperty: "eventId"),

        ["events.event.cancelled.v1"] = new(
            Action: "EventCancelled",
            EntityName: "Event",
            EntityIdProperty: "eventId"),

        ["pathshala.created.v1"] = new(
            Action: "PathshalaCreated",
            EntityName: "Pathshala",
            EntityIdProperty: "pathshalaId"),

        ["pathshala.session.opened.v1"] = new(
            Action: "AcademicSessionOpened",
            EntityName: "AcademicSession",
            EntityIdProperty: "sessionId"),

        ["pathshala.deactivated.v1"] = new(
            Action: "PathshalaDeactivated",
            EntityName: "Pathshala",
            EntityIdProperty: "pathshalaId"),

        // The one topic in this batch with the same "distinct actor" shape as
        // the earlier finds: a child cannot ask for their own place, so
        // RequestedByMemberId - the parent - is a different person from the
        // child (ChildProfileId) the request is about.
        ["pathshala.enrolment.requested.v1"] = new(
            Action: "EnrolmentRequested",
            EntityName: "Enrolment",
            EntityIdProperty: "enrolmentId",
            ActorIdProperty: "requestedByMemberId"),

        // No PlacedBy on this event - placement is a Pathshala administrator's
        // decision at a point this event does not capture the actor of.
        ["pathshala.student.enrolled.v1"] = new(
            Action: "StudentEnrolled",
            EntityName: "Enrolment",
            EntityIdProperty: "enrolmentId"),

        // No RecordedBy on this event either, deliberately: the child is
        // named only by an enrolment id per its own doc comment, and the
        // teacher who marked it was never added to the payload.
        ["pathshala.exam-result.recorded.v1"] = new(
            Action: "ExamResultRecorded",
            EntityName: "Enrolment",
            EntityIdProperty: "enrolmentId"),

        // Erasure is handled by ErasePersonalDataCommandHandler rather than
        // recorded through this path, and it writes its own row with no actor
        // deliberately. Listed here so the omission reads as a decision.
        ["identity.user.erased.v1"] = new(
            Action: "Erased",
            EntityName: "User",
            EntityIdProperty: "userId"),
    };

    public static EventDescriptor Describe(string topic) =>
        Descriptors.TryGetValue(topic, out var descriptor)
            ? descriptor
            : new EventDescriptor(DeriveAction(topic), DeriveEntityName(topic), EntityIdProperty: null);

    /// <summary>
    /// Turns "shop.order.line-added.v1" into "LineAdded" so an unknown event
    /// still reads sensibly in the audit log.
    /// </summary>
    private static string DeriveAction(string topic)
    {
        var segments = topic.Split('.', StringSplitOptions.RemoveEmptyEntries);

        // Trim a trailing version segment such as "v1".
        if (segments.Length > 1 && segments[^1].Length > 1
            && segments[^1][0] is 'v' or 'V'
            && segments[^1][1..].All(char.IsDigit))
        {
            segments = segments[..^1];
        }

        return segments.Length == 0 ? "Unknown" : ToPascalCase(segments[^1]);
    }

    private static string DeriveEntityName(string topic)
    {
        var segments = topic.Split('.', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length < 2 ? "Unknown" : ToPascalCase(segments[1]);
    }

    private static string ToPascalCase(string segment) =>
        string.Concat(segment
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
