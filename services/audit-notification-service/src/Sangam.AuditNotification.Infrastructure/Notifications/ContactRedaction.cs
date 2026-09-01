namespace Sangam.AuditNotification.Infrastructure.Notifications;

/// <summary>
/// Shortens a contact address to something that can be written to a log.
/// </summary>
/// <remarks>
/// An operator reading logs needs to tell one recipient from another and to
/// recognise a plainly wrong address. Neither needs the whole thing, and the
/// whole thing is what makes a log file a copy of the member directory.
///
/// The output keeps enough to compare against an address you already know and
/// too little to learn one you do not. Email keeps the first character and the
/// domain, which is the convention every provider uses on a "we sent it to
/// d***@example.com" screen. A number keeps its last three digits, the same
/// convention a bank uses.
/// </remarks>
public static class ContactRedaction
{
    public static string Redact(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return "(none)";
        }

        var value = destination.Trim();
        var at = value.IndexOf('@');

        return at > 0 ? RedactEmail(value, at) : RedactNumber(value);
    }

    private static string RedactEmail(string value, int at)
    {
        var local = value[..at];
        var domain = value[at..];

        // A one-character local part has nothing to keep: showing it would be
        // showing the whole local part.
        return local.Length <= 1 ? $"***{domain}" : $"{local[0]}***{domain}";
    }

    private static string RedactNumber(string value)
    {
        var digits = value.Where(char.IsAsciiDigit).ToArray();

        if (digits.Length <= 3)
        {
            return "***";
        }

        return $"***{new string(digits[^3..])}";
    }
}
