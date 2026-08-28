using System.Text.Json;

namespace Sangam.AuditNotification.Application.IntegrationEvents;

/// <summary>How one topic should be recorded, and whether it deserves a notification.</summary>
public sealed record EventDescriptor(
    string Action,
    string EntityName,
    string? EntityIdProperty,
    string? ActorIdProperty = null,
    Func<JsonElement, NotificationSpec?>? Notification = null);

public sealed record NotificationSpec(Guid? RecipientUserId, string Title, string Body);

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
            EntityIdProperty: "tenantId"),

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
                        : $"Welcome, {name}. Complete your profile to appear in the member directory.");
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

        ["identity.tenant.modules-changed.v1"] = new(
            Action: "TenantModulesChanged",
            EntityName: "Tenant",
            EntityIdProperty: "tenantId"),

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
