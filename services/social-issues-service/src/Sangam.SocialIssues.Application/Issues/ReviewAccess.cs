using Sangam.SocialIssues.Application.Abstractions;
using Sangam.SocialIssues.Application.Security;
using Sangam.SocialIssues.Domain.Issues;

namespace Sangam.SocialIssues.Application.Issues;

/// <summary>
/// What this caller may do about issues that are waiting on a reviewer.
/// </summary>
/// <param name="CanSeeQueue">
/// May read the review queue and any issue in it: a seated Panch member, or a
/// holder of <c>SocialIssues.Approve</c>. An administrator keeps sight of the
/// queue once a Panch exists - somebody has to be able to tell whether it is
/// being worked - but not the power to act on it.
/// </param>
/// <param name="CanAct">
/// May move an issue and record a decision: a seated Panch member, or - only
/// while <b>no</b> Panch is seated - a holder of <c>SocialIssues.Approve</c>.
/// </param>
/// <param name="PanelSeated">Whether this Samaaj has elected a Panch.</param>
/// <param name="IsPanelMember">Whether the caller sits on it.</param>
/// <param name="ApproveLocalityId">
/// Set when the caller's <c>SocialIssues.Approve</c> comes from being an Area
/// Admin: it then reaches only issues in that locality. A Panch seat is never
/// narrowed this way - the Panch judges for the whole Samaaj.
/// </param>
/// <remarks>
/// <see cref="CanSeeQueue"/> and <see cref="CanAct"/> answer for the caller in
/// general, which is what the portal needs to decide whether to offer a queue
/// at all. Anything about a particular issue asks <see cref="MaySee"/> and
/// <see cref="MayAct"/>, which also apply the locality.
/// </remarks>
public sealed record ReviewAccess(
    bool CanSeeQueue,
    bool CanAct,
    bool PanelSeated,
    bool IsPanelMember,
    Guid? ApproveLocalityId = null)
{
    /// <summary>Nobody, for a caller with no identity.</summary>
    public static ReviewAccess None { get; } = new(false, false, false, false);

    /// <summary>May read this issue as a reviewer - in the queue, or by id.</summary>
    public bool MaySee(SocialIssue issue) =>
        IsPanelMember || (CanSeeQueue && CoversByApprove(issue));

    /// <summary>May move this issue as a reviewer, or record its decision.</summary>
    public bool MayAct(SocialIssue issue) =>
        IsPanelMember || (CanAct && CoversByApprove(issue));

    private bool CoversByApprove(SocialIssue issue) =>
        ApproveLocalityId is null || issue.LocalityId == ApproveLocalityId;
}

/// <summary>
/// Works out a <see cref="ReviewAccess"/>, in the one place, so the answer to
/// "who reviews an issue" cannot differ between the queue, the detail, and the
/// move handler.
/// </summary>
/// <remarks>
/// <b>The Panch reviews once there is one.</b> Only its members can approve,
/// reject or send back an issue, and only they can decide it. Until a Samaaj has
/// elected a Panch - and the module is off by default, so most have not - the
/// holders of <c>SocialIssues.Approve</c> review as they always did; otherwise
/// every issue in every such Samaaj would stall the day this shipped.
///
/// The moment a Panch is seated the fallback closes. Leaving it open would make
/// "only the Panch judges" a rule with an exception for administrators, which is
/// a rule that does not hold.
/// </remarks>
public sealed class ReviewAccessResolver(
    IPanelRepository panel, ICurrentUser currentUser, IAreaScope areaScope)
{
    public async Task<ReviewAccess> ResolveAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return ReviewAccess.None;
        }

        var holdsApprove = currentUser.HasPermission(PermissionKeys.SocialIssuesApprove);
        var isMember = await panel.IsSeatedAsync(memberId, cancellationToken);
        var seated = isMember || await panel.AnySeatedAsync(cancellationToken);

        return new ReviewAccess(
            CanSeeQueue: isMember || holdsApprove,
            CanAct: isMember || (holdsApprove && !seated),
            PanelSeated: seated,
            IsPanelMember: isMember,
            ApproveLocalityId: areaScope.AreaLocalityId);
    }
}
