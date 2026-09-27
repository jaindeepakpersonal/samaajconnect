namespace Sangam.Trust.Domain.Trusts;

/// <summary>
/// One role a trustee can hold - President, Secretary, Treasurer, or plain
/// Trustee. Owned by the <see cref="Trust"/>; which member holds it is on
/// their <c>TrustMember</c> row.
/// </summary>
/// <remarks>
/// <b>Trustee</b> is built into every Trust: every trustee holds it until they
/// hold something else, it has no seat limit, and nobody is elected to it -
/// the same shape as a volunteer group's Volunteer role. Every other role is
/// the Trust's own, defined with the number of seats an election for it fills.
/// </remarks>
public sealed class TrustRole
{
    public Guid Id { get; private set; }

    public Guid TrustId { get; private set; }

    public string Title { get; private set; } = null!;

    public TrustRoleKind Kind { get; private set; }

    /// <summary>How many hold it at most - and how many an election fills. Null for Trustee.</summary>
    public int? Seats { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private TrustRole() { }   // EF Core

    internal TrustRole(Guid trustId, string title, TrustRoleKind kind, int? seats, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        TrustId = trustId;
        Title = title.Trim();
        Kind = kind;
        Seats = seats;
        CreatedAt = createdAt;
    }

    /// <summary>Elected to, and editable. False only for the built-in Trustee role.</summary>
    public bool IsElected => Kind == TrustRoleKind.Elected;

    internal void Change(string title, int seats)
    {
        Title = title.Trim();
        Seats = seats;
    }
}

public enum TrustRoleKind
{
    /// <summary>The built-in default every trustee holds until they hold another.</summary>
    Trustee = 1,

    /// <summary>A role the Trust defined, with seats, filled by election or by hand.</summary>
    Elected = 2,
}

public enum TrustRoleRefusal
{
    None = 0,
    NotFound,
    DuplicateTitle,
    NotEditable,
    SeatsBelowHolders,
    HasHolders,
    RoleFull,
    NotElected,
}
