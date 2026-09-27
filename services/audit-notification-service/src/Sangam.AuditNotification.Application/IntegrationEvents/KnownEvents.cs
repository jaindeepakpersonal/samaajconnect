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
        // The other half of the same shape as identity.login-otp.requested.v1
        // - a plaintext secret in the Notification body, Destination always
        // set, because there is no admin standing between minting this code
        // and delivering it either.
        ["identity.password-reset.requested.v1"] = new(
            Action: "PasswordResetRequested",
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
                    "Reset your password",
                    $"Your password reset code is {code.GetString()}. It expires in 10 minutes.",
                    Destination: payload.TryGetProperty("mobileOrEmail", out var contact)
                        ? contact.GetString()
                        : null);
            }),

        // Distinct from identity.user.password-changed.v1 - that one is an
        // authenticated member changing a password they already know; this
        // one is an anonymous code redeemed instead. In-app only: the member
        // is not signed in wherever this notification appears, but they are
        // signed in wherever they still hold a session that has not yet been
        // revoked, which is exactly who should see it.
        ["identity.password-reset.completed.v1"] = new(
            Action: "PasswordReset",
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
                    "Your password was reset",
                    "Your password was reset just now. If this wasn't you, contact your "
                    + "Samaaj administrator.");
            }),

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

        // A group's own volunteer roles - defined, renamed or resized, removed -
        // by its president or an administrator. Each names who, because both
        // can, and "who added a Treasurer role?" should not need guessing.
        ["volunteer-groups.role.defined.v1"] = new(
            Action: "RoleDefined",
            EntityName: "GroupRole",
            EntityIdProperty: "roleId",
            ActorIdProperty: "changedBy"),

        ["volunteer-groups.role.updated.v1"] = new(
            Action: "RoleUpdated",
            EntityName: "GroupRole",
            EntityIdProperty: "roleId",
            ActorIdProperty: "changedBy"),

        ["volunteer-groups.role.removed.v1"] = new(
            Action: "RoleRemoved",
            EntityName: "GroupRole",
            EntityIdProperty: "roleId",
            ActorIdProperty: "changedBy"),

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

        ["volunteer-groups.group.updated.v1"] = new(
            Action: "GroupUpdated",
            EntityName: "Group",
            EntityIdProperty: "groupId"),

        ["volunteer-groups.election.updated.v1"] = new(
            Action: "ElectionUpdated",
            EntityName: "GroupElection",
            EntityIdProperty: "electionId"),

        ["volunteer-groups.election.status-changed.v1"] = new(
            Action: "ElectionStatusChanged",
            EntityName: "GroupElection",
            EntityIdProperty: "electionId",
            BeforeProperties: ["previousStatus"]),

        ["volunteer-groups.election.closed.v1"] = new(
            Action: "ElectionClosed",
            EntityName: "GroupElection",
            EntityIdProperty: "electionId"),

        // WinnerMemberId is who the event is about, not who published it - the
        // event carries no publisher id at all, the same shape celebrity-
        // voting-service's own ResultsPublishedDomainEvent already has.
        ["volunteer-groups.election.results-published.v1"] = new(
            Action: "ElectionResultsPublished",
            EntityName: "GroupElection",
            EntityIdProperty: "electionId"),

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

        // Retired: nothing raises this any more. Issues are put in front of the
        // Samaaj Panch and decided rather than published. The descriptor stays
        // because rows already in the audit trail were written under it.
        ["social-issues.issue.published.v1"] = new(
            Action: "IssuePublished",
            EntityName: "Issue",
            EntityIdProperty: "issueId"),

        // The Panch decided an issue. It names who decided: "who closed my issue,
        // and what did they decide to do about it" is what this trail exists to
        // answer, and the status change beside it names the same person.
        ["social-issues.issue.decided.v1"] = new(
            Action: "IssueDecided",
            EntityName: "Issue",
            EntityIdProperty: "issueId",
            ActorIdProperty: "decidedByMemberId"),

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
            ActorIdProperty: "familyAdminMemberId"),

        ["members.family.head-changed.v1"] = new(
            Action: "FamilyHeadChanged",
            EntityName: "Family",
            EntityIdProperty: "familyId",
            ActorIdProperty: "changedBy"),

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

        // Corrections name who made them; the details stay on the occasion.
        ["boli.occasion.updated.v1"] = new(
            Action: "OccasionUpdated",
            EntityName: "Occasion",
            EntityIdProperty: "occasionId",
            ActorIdProperty: "updatedBy"),

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

        ["celebrity-voting.campaign.updated.v1"] = new(
            Action: "CampaignUpdated",
            EntityName: "Campaign",
            EntityIdProperty: "campaignId"),

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

        ["events.event.updated.v1"] = new(
            Action: "EventUpdated",
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

        ["pathshala.updated.v1"] = new(
            Action: "PathshalaUpdated",
            EntityName: "Pathshala",
            EntityIdProperty: "pathshalaId"),

        // A Pathshala's teachers - who need not be members. Ids only: their
        // names and phones are in pathshala-service, where they can be corrected.
        ["pathshala.teacher.added.v1"] = new(
            Action: "TeacherAdded",
            EntityName: "Teacher",
            EntityIdProperty: "teacherId",
            ActorIdProperty: "changedBy"),

        ["pathshala.teacher.updated.v1"] = new(
            Action: "TeacherUpdated",
            EntityName: "Teacher",
            EntityIdProperty: "teacherId",
            ActorIdProperty: "changedBy"),

        ["pathshala.teacher.removed.v1"] = new(
            Action: "TeacherRemoved",
            EntityName: "Teacher",
            EntityIdProperty: "teacherId",
            ActorIdProperty: "changedBy"),

        // The donation ledger. Money recorded and money taken back out of the
        // totals each name who did it; the donor's name and the purpose stay
        // in the ledger.
        ["pathshala.donation.recorded.v1"] = new(
            Action: "DonationRecorded",
            EntityName: "PathshalaDonation",
            EntityIdProperty: "donationId",
            ActorIdProperty: "recordedBy"),

        ["pathshala.donation.voided.v1"] = new(
            Action: "DonationVoided",
            EntityName: "PathshalaDonation",
            EntityIdProperty: "donationId",
            ActorIdProperty: "voidedBy"),

        ["pathshala.announcement.sent.v1"] = new(
            Action: "AnnouncementSent",
            EntityName: "PathshalaAnnouncement",
            EntityIdProperty: "announcementId",
            ActorIdProperty: "sentBy"),

        // One per parent. The recipient is who it is about, not who acted, so
        // no ActorIdProperty. The notification says there is something to
        // read and never what: the text is the Pathshala's, read where it can
        // be corrected, never copied into a notification or this log.
        ["pathshala.announcement.addressed.v1"] = new(
            Action: "AnnouncementAddressed",
            EntityName: "PathshalaAnnouncement",
            EntityIdProperty: "announcementId",
            Notification: payload =>
                payload.TryGetProperty("recipientMemberId", out var recipient)
                && recipient.TryGetGuid(out var recipientId)
                    ? new NotificationSpec(
                        recipientId,
                        "A message from your child's Pathshala",
                        "Your child's Pathshala has sent an announcement. Open Jain Pathshala to read it.")
                    : null),

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

        // master-data-service, the eleventh service. None of its twelve
        // topics has an actor to name distinct from the row itself - these are
        // platform-wide reference data (Country/City/Locality/ProfessionType/
        // Gender/MaritalStatus, folded into one LookupItem shape - see that
        // service's own CLAUDE.md for why) and one Samaaj's own Gautra list,
        // maintained by whichever administrator happened to be signed in
        // rather than a decision made *about* somebody else. Giving each an
        // entity id is still the same real improvement over the derived
        // default that the last eighteen topics found: searchable by entity,
        // not just present.
        ["master-data.lookup-item.created.v1"] = new(
            Action: "LookupItemCreated",
            EntityName: "LookupItem",
            EntityIdProperty: "lookupItemId"),

        ["master-data.lookup-item.updated.v1"] = new(
            Action: "LookupItemUpdated",
            EntityName: "LookupItem",
            EntityIdProperty: "lookupItemId"),

        ["master-data.lookup-item.deactivated.v1"] = new(
            Action: "LookupItemDeactivated",
            EntityName: "LookupItem",
            EntityIdProperty: "lookupItemId"),

        ["master-data.state.created.v1"] = new(
            Action: "StateCreated",
            EntityName: "State",
            EntityIdProperty: "stateId"),

        ["master-data.state.updated.v1"] = new(
            Action: "StateUpdated",
            EntityName: "State",
            EntityIdProperty: "stateId"),

        ["master-data.state.deactivated.v1"] = new(
            Action: "StateDeactivated",
            EntityName: "State",
            EntityIdProperty: "stateId"),

        ["master-data.city.created.v1"] = new(
            Action: "CityCreated",
            EntityName: "City",
            EntityIdProperty: "cityId"),

        ["master-data.city.updated.v1"] = new(
            Action: "CityUpdated",
            EntityName: "City",
            EntityIdProperty: "cityId"),

        ["master-data.city.deactivated.v1"] = new(
            Action: "CityDeactivated",
            EntityName: "City",
            EntityIdProperty: "cityId"),

        ["master-data.locality.created.v1"] = new(
            Action: "LocalityCreated",
            EntityName: "Locality",
            EntityIdProperty: "localityId"),

        ["master-data.locality.updated.v1"] = new(
            Action: "LocalityUpdated",
            EntityName: "Locality",
            EntityIdProperty: "localityId"),

        ["master-data.locality.deactivated.v1"] = new(
            Action: "LocalityDeactivated",
            EntityName: "Locality",
            EntityIdProperty: "localityId"),

        // Gautra is the one tenant-scoped aggregate here, but that changes
        // nothing about who the actor is: the event still names only the
        // Gautra and the Samaaj it belongs to, the same shape as the three
        // platform-wide aggregates above.
        ["master-data.gautra.created.v1"] = new(
            Action: "GautraCreated",
            EntityName: "Gautra",
            EntityIdProperty: "gautraId"),

        ["master-data.gautra.updated.v1"] = new(
            Action: "GautraUpdated",
            EntityName: "Gautra",
            EntityIdProperty: "gautraId"),

        ["master-data.gautra.deactivated.v1"] = new(
            Action: "GautraDeactivated",
            EntityName: "Gautra",
            EntityIdProperty: "gautraId"),

        // matrimony-service, the twelfth service. Opting in and withdrawing are
        // both self-actions - the same shape as family.created.v1 - so neither
        // carries a distinct actor. A proposal's own two topics are the
        // opposite: EntityId names the Proposal, but the sender and recipient
        // are two different people, so each event's actor is whichever of them
        // performed that particular act rather than the proposal's own id.
        ["matrimony.biodata.created.v1"] = new(
            Action: "BiodataCreated",
            EntityName: "BiodataProfile",
            EntityIdProperty: "memberId"),

        ["matrimony.consent.withdrawn.v1"] = new(
            Action: "MatrimonyConsentWithdrawn",
            EntityName: "BiodataProfile",
            EntityIdProperty: "memberId"),

        ["matrimony.proposal.sent.v1"] = new(
            Action: "ProposalSent",
            EntityName: "Proposal",
            EntityIdProperty: "proposalId",
            ActorIdProperty: "fromMemberId"),

        // Outcome ("Accepted"/"Declined") is the fact worth keeping as
        // before/after context - the row already carries the new state as its
        // action name, so BeforeProperties would only repeat "Pending", which
        // every proposal starts as. ActorIdProperty is the recipient: deciding
        // is their act, not the sender's.
        ["matrimony.proposal.responded.v1"] = new(
            Action: "ProposalResponded",
            EntityName: "Proposal",
            EntityIdProperty: "proposalId",
            ActorIdProperty: "toMemberId"),

        ["matrimony.proposal.withdrawn.v1"] = new(
            Action: "ProposalWithdrawn",
            EntityName: "Proposal",
            EntityIdProperty: "proposalId",
            ActorIdProperty: "fromMemberId"),

        // businesses-service, the thirteenth service. Creating and
        // deactivating a business are both self-actions, the same shape as
        // matrimony.biodata.created.v1 - the owner is both the entity and
        // the actor, so neither carries a distinct ActorIdProperty. Placing
        // an order is the buyer's own act (ActorIdProperty: buyerMemberId).
        // Responding and fulfilling are always the business owner's act -
        // found live, on 2026-09-12, when a real audit row for
        // OrderResponded/OrderFulfilled named the order and nothing else:
        // the domain events did not yet carry the owner's id at all, so
        // there was no field here to point at even for a caller wanting to
        // know who accepted or fulfilled an order. Both events now carry
        // BusinessOwnerMemberId for exactly that. Cancel is the one topic
        // that stays without an actor: either the buyer (while Pending) or
        // the owner (while Accepted) can raise it, and naming one
        // unconditionally would be wrong for the other half of the time -
        // the same restraint post.reported.v1 already takes for a field
        // this catalogue would otherwise have to guess at.
        ["businesses.business.created.v1"] = new(
            Action: "BusinessCreated",
            EntityName: "Business",
            EntityIdProperty: "businessId"),

        ["businesses.business.status-changed.v1"] = new(
            Action: "BusinessStatusChanged",
            EntityName: "Business",
            EntityIdProperty: "businessId"),

        ["businesses.order.placed.v1"] = new(
            Action: "OrderPlaced",
            EntityName: "Order",
            EntityIdProperty: "orderId",
            ActorIdProperty: "buyerMemberId"),

        ["businesses.order.responded.v1"] = new(
            Action: "OrderResponded",
            EntityName: "Order",
            EntityIdProperty: "orderId",
            ActorIdProperty: "businessOwnerMemberId"),

        ["businesses.order.fulfilled.v1"] = new(
            Action: "OrderFulfilled",
            EntityName: "Order",
            EntityIdProperty: "orderId",
            ActorIdProperty: "businessOwnerMemberId"),

        ["businesses.order.cancelled.v1"] = new(
            Action: "OrderCancelled",
            EntityName: "Order",
            EntityIdProperty: "orderId"),

        // competition-service, the fourteenth service. Status changes and
        // round closes/publishes are all the organiser's own act under
        // Competition.Manage, the same "no distinct actor to name" shape
        // celebrity-voting.campaign.*.v1 already takes - neither event
        // carries a separate actor id, so there is none to point at.
        // Registering is the member's own self-action, the same shape
        // businesses.business.created.v1 takes: the member is both the
        // entity and the actor, so no distinct ActorIdProperty either.
        ["competition.competition.status-changed.v1"] = new(
            Action: "CompetitionStatusChanged",
            EntityName: "Competition",
            EntityIdProperty: "competitionId",
            BeforeProperties: ["previousStatus"]),

        ["competition.competition.updated.v1"] = new(
            Action: "CompetitionUpdated",
            EntityName: "Competition",
            EntityIdProperty: "competitionId",
            ActorIdProperty: "updatedBy"),

        ["competition.registration.created.v1"] = new(
            Action: "RegistrationCreated",
            EntityName: "CompetitionRegistration",
            EntityIdProperty: "registrationId"),

        ["competition.round.closed.v1"] = new(
            Action: "RoundClosed",
            EntityName: "Round",
            EntityIdProperty: "roundId"),

        ["competition.round.results-published.v1"] = new(
            Action: "RoundResultsPublished",
            EntityName: "Round",
            EntityIdProperty: "roundId"),
        // panch-service. An election moving on is the administrator's own act
        // and names no separate actor, the shape competition.* and
        // celebrity-voting.campaign.* take. A change to the panel does name one:
        // the administrator who removed a member, or who published the election
        // that seated it - "who decided who sits on the Panch" is the question
        // this trail exists to answer. The panel has no id of its own; it is
        // the Samaaj's, so the tenant is what it is recorded against.
        ["panch.election.status-changed.v1"] = new(
            Action: "ElectionStatusChanged",
            EntityName: "PanchElection",
            EntityIdProperty: "electionId",
            BeforeProperties: ["previousStatus"]),

        ["panch.panel.changed.v1"] = new(
            Action: "PanelChanged",
            EntityName: "PanchPanel",
            EntityIdProperty: "tenantId",
            ActorIdProperty: "changedBy"),

        // trust-service, phase 1 (roster only - elections, donations, expenses
        // and payment collection are later phases). Creating a Trust and
        // changing its status are the administrator's own act and name no
        // separate actor, the same shape panch.election.status-changed.v1
        // takes. A roster change does name one - who put a member on a Trust,
        // who changed their position, who took them off - the same "who did
        // this" the volunteer-groups.member.* pair already answers for an
        // identical shape of question.
        ["trust.trust.created.v1"] = new(
            Action: "TrustCreated",
            EntityName: "Trust",
            EntityIdProperty: "trustId"),

        ["trust.trust.status-changed.v1"] = new(
            Action: "TrustStatusChanged",
            EntityName: "Trust",
            EntityIdProperty: "trustId",
            BeforeProperties: ["previousStatus"]),

        ["trust.trust.renamed.v1"] = new(
            Action: "TrustRenamed",
            EntityName: "Trust",
            EntityIdProperty: "trustId",
            ActorIdProperty: "renamedBy"),

        ["trust.member.added.v1"] = new(
            Action: "MemberAdded",
            EntityName: "TrustMember",
            EntityIdProperty: "trustMemberId",
            ActorIdProperty: "addedBy"),

        ["trust.member.updated.v1"] = new(
            Action: "MemberUpdated",
            EntityName: "TrustMember",
            EntityIdProperty: "trustMemberId",
            ActorIdProperty: "updatedBy"),

        ["trust.member.removed.v1"] = new(
            Action: "MemberRemoved",
            EntityName: "TrustMember",
            EntityIdProperty: "trustMemberId",
            ActorIdProperty: "removedBy"),

        // trust-service, phase 2 (elections for trustee positions). Moving an
        // election is the administrator's own act and names no separate
        // actor, the identical shape panch.election.status-changed.v1 and
        // volunteer-groups.election.status-changed.v1 already take.
        ["trust.election.status-changed.v1"] = new(
            Action: "ElectionStatusChanged",
            EntityName: "TrustElection",
            EntityIdProperty: "electionId",
            BeforeProperties: ["previousStatus"]),

        // WinnerMemberId is who the event is about, not who published it -
        // the event carries no publisher id at all, the same shape
        // volunteer-groups.election.results-published.v1 already has.
        // Publishing also installs the winner on their own TrustMember row,
        // which raises its own trust.member.updated.v1 with UpdatedBy - "who
        // did this" is answered there, not here.
        ["trust.election.results-published.v1"] = new(
            Action: "ElectionResultsPublished",
            EntityName: "TrustElection",
            EntityIdProperty: "electionId"),

        // trust-service, phase 3 (money). Every write here moves or approves
        // money, so each one that a person performs names them: who recorded
        // a donation or an expense, who collected it, who approved it. The
        // donor is who a donation is *about*, not who recorded it, the same
        // subject/actor split trust.member.added.v1 draws with addedBy.
        ["trust.donation.recorded.v1"] = new(
            Action: "DonationRecorded",
            EntityName: "Donation",
            EntityIdProperty: "donationId",
            ActorIdProperty: "recordedBy"),

        ["trust.donation.collected.v1"] = new(
            Action: "DonationCollected",
            EntityName: "Donation",
            EntityIdProperty: "donationId",
            ActorIdProperty: "collectedBy"),

        ["trust.expense.recorded.v1"] = new(
            Action: "ExpenseRecorded",
            EntityName: "Expense",
            EntityIdProperty: "expenseId",
            ActorIdProperty: "recordedBy"),

        // The approver is deliberately a second person - that is what an
        // approval is for - so naming them is the half of this row that
        // matters.
        ["trust.expense.approved.v1"] = new(
            Action: "ExpenseApproved",
            EntityName: "Expense",
            EntityIdProperty: "expenseId",
            ActorIdProperty: "approvedBy"),

        // A payment falling due is raised by what caused it - a pledge, or a
        // Boli result this Trust's locality won - rather than by somebody
        // acting, so it carries no actor; sourceType/sourceId say where it
        // came from. Settling it is a person's act and names them.
        ["trust.collection.due.v1"] = new(
            Action: "CollectionDue",
            EntityName: "Collection",
            EntityIdProperty: "collectionId"),

        ["trust.collection.settled.v1"] = new(
            Action: "CollectionSettled",
            EntityName: "Collection",
            EntityIdProperty: "collectionId",
            ActorIdProperty: "collectedBy"),

        // A Trust officer chasing an amount due. The officer is the actor; the
        // payer is who it is addressed to. The notification says something is
        // due and what for, never how much: the amount is on the member's own
        // Trusts screen, and a figure here is a figure in an inbox anybody
        // glancing at the phone can read.
        ["trust.collection.reminded.v1"] = new(
            Action: "CollectionReminded",
            EntityName: "Collection",
            EntityIdProperty: "collectionId",
            ActorIdProperty: "remindedBy",
            Notification: payload =>
            {
                if (!payload.TryGetProperty("payerMemberId", out var payer)
                    || !payer.TryGetGuid(out var recipientId))
                {
                    return null;
                }

                var forWhat = payload.TryGetProperty("sourceType", out var source)
                    && source.GetString() == "BoliResult"
                        ? "a Boli you won"
                        : "your pledge";

                return new NotificationSpec(
                    recipientId,
                    "A reminder from your Trust",
                    $"A payment for {forWhat} is still due. Open Trusts to see how much.");
            }),
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
