namespace Sangam.Trust.Application.Members;

/// <summary>One row on a Trust's roster.</summary>
public sealed record TrustMemberResponse(
    Guid Id,
    Guid TrustId,
    Guid MemberId,

    /// <summary>The title of the role they hold - "Trustee" unless they hold another.</summary>
    string? PositionTitle,
    string Status,
    Guid AddedBy,
    DateTimeOffset AddedAt,
    Guid? RemovedBy,
    DateTimeOffset? RemovedAt,
    Guid RoleId);
