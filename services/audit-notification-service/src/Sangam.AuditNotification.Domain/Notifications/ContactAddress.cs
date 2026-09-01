namespace Sangam.AuditNotification.Domain.Notifications;

/// <summary>
/// Works out which channel can carry a message to a given contact.
/// </summary>
/// <remarks>
/// The platform stores one <c>MobileOrEmail</c> per login rather than separate
/// email and phone columns (identity-tenant-service's <c>User</c>), so the only
/// thing that can decide whether an address is reachable by email or by SMS is
/// the shape of the address itself. That is why this exists rather than the
/// caller simply naming a channel: the caller usually does not know either.
///
/// It is deliberately conservative. Anything it cannot confidently classify
/// comes back null, and a notification with no channel is recorded as failed
/// with a reason rather than guessed at and sent somewhere wrong.
/// </remarks>
public static class ContactAddress
{
    public static NotificationChannel? ChannelFor(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return null;
        }

        var trimmed = destination.Trim();

        if (LooksLikeEmail(trimmed))
        {
            return NotificationChannel.Email;
        }

        return LooksLikeMobile(trimmed) ? NotificationChannel.Sms : null;
    }

    /// <summary>
    /// One '@' with something either side, and a dot in the domain. Not RFC
    /// 5322 - it does not need to be. This decides which way to send, and the
    /// send itself is what discovers an address is undeliverable.
    /// </summary>
    private static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@');

        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1)
        {
            return false;
        }

        var domain = value[(at + 1)..];

        return domain.Contains('.', StringComparison.Ordinal)
            && !domain.StartsWith('.')
            && !domain.EndsWith('.')
            && !value.Contains(' ', StringComparison.Ordinal);
    }

    /// <summary>
    /// Digits, optionally with a leading '+' and the separators people type.
    /// Between 8 and 15 digits, the E.164 maximum, so a membership number or a
    /// year is not mistaken for a phone number.
    /// </summary>
    private static bool LooksLikeMobile(string value)
    {
        var digits = 0;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (char.IsAsciiDigit(c))
            {
                digits++;
            }
            else if (c is '+' && i == 0)
            {
                continue;
            }
            else if (c is not (' ' or '-' or '(' or ')'))
            {
                return false;
            }
        }

        return digits is >= 8 and <= 15;
    }
}
