using FluentValidation;
using Sangam.Timeline.Application.Common;
using Sangam.Timeline.Application.Security;
using Sangam.Timeline.Domain.Posts;

namespace Sangam.Timeline.Application.Posts.Commands.ModeratePost;

/// <summary>
/// Approve, reject, hide or restore a post.
/// </summary>
/// <remarks>
/// The decision is a required field with no default. A moderation endpoint
/// whose safest value is implicit is one where a mis-typed request quietly
/// publishes something.
/// </remarks>
[RequiresPermission(PermissionKeys.TimelineModerate)]
public sealed record ModeratePostCommand(Guid PostId, string Decision, string? Reason)
    : ICommand<PostResponse>;

public sealed class ModeratePostCommandValidator : AbstractValidator<ModeratePostCommand>
{
    public ModeratePostCommandValidator()
    {
        RuleFor(x => x.PostId).NotEmpty();

        RuleFor(x => x.Decision)
            .NotEmpty()
            .Must(d => Enum.TryParse<ModerationDecision>(d, ignoreCase: true, out _))
            .WithMessage("Decision must be one of Approve, Reject, Hide or Restore.");

        RuleFor(x => x.Reason).MaximumLength(1000);

        // Refusing or removing something a member wrote is the case where they
        // will ask why, so the note is not optional there. Approving needs no
        // explanation.
        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Say why. The member is told this, and \"no reason given\" is not an answer.")
            .When(x => x.Decision is not null
                && Enum.TryParse<ModerationDecision>(x.Decision, ignoreCase: true, out var parsed)
                && parsed is ModerationDecision.Reject or ModerationDecision.Hide);
    }
}
