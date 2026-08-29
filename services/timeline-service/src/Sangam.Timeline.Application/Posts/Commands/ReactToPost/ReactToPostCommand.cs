using FluentValidation;
using MediatR;
using Sangam.Timeline.Application.Abstractions;
using Sangam.Timeline.Application.Common;
using Sangam.Timeline.Application.Security;
using Sangam.Timeline.Domain.Posts;

namespace Sangam.Timeline.Application.Posts.Commands.ReactToPost;

/// <summary>
/// Sets, changes or clears this member's reaction. Sending the reaction they
/// already hold removes it, which is how every product with this button behaves.
/// </summary>
[RequiresPermission(PermissionKeys.TimelinePost)]
public sealed record ReactToPostCommand(Guid PostId, string Reaction) : ICommand<PostResponse>;

public sealed class ReactToPostCommandValidator : AbstractValidator<ReactToPostCommand>
{
    public ReactToPostCommandValidator()
    {
        RuleFor(x => x.PostId).NotEmpty();

        RuleFor(x => x.Reaction)
            .NotEmpty()
            .Must(r => Enum.TryParse<ReactionType>(r, ignoreCase: true, out _))
            .WithMessage("Reaction must be one of Appreciate, Support or Celebrate.");
    }
}

public sealed class ReactToPostCommandHandler(
    IPostRepository posts,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<ReactToPostCommand, Result<PostResponse>>
{
    public async Task<Result<PostResponse>> Handle(
        ReactToPostCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<PostResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var post = await posts.GetByIdAsync(command.PostId, cancellationToken);

        if (post is null
            || (tenantContext.TenantId is { } tenantId && post.TenantId != tenantId)
            || !post.IsPubliclyVisible)
        {
            return Result.Failure<PostResponse>(
                Error.NotFound("Post.NotFound", "No such post in this Samaaj."));
        }

        post.React(memberId, Enum.Parse<ReactionType>(command.Reaction, ignoreCase: true), clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(post.ToResponse(memberId));
    }
}
