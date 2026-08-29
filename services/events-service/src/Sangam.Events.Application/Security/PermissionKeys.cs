namespace Sangam.Events.Application.Security;

/// <summary>
/// Permission keys this service gates on, in the platform's {Module}.{Action}
/// convention (SECURITY-CHECKLIST.md). Minted by identity-tenant-service and
/// arriving as token claims.
/// </summary>
public static class PermissionKeys
{
    /// <summary>
    /// Create, publish and cancel events. Samaaj admins and volunteer group
    /// presidents hold it - the two kinds of organiser the data model names.
    /// </summary>
    /// <remarks>
    /// Unlike volunteer-groups-service, there is no second permission for
    /// "organiser of this particular event": whoever may publish an event may
    /// publish one, and the handler checks that they created *this* one before
    /// letting them change it. The distinction there existed because a group's
    /// president is an ordinary member; here both holders are already
    /// administrators of something.
    /// </remarks>
    public const string EventsPublish = "Events.Publish";

    /// <summary>
    /// See the Samaaj's events and register for them. Every member holds it,
    /// via Members.Read.
    /// </summary>
    public const string MembersRead = "Members.Read";
}
