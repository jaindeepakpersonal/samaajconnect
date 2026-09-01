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
