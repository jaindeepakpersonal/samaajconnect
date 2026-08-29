using FluentValidation;
using MediatR;
using Sangam.Timeline.Application.Abstractions;
using Sangam.Timeline.Application.Common;
using Sangam.Timeline.Application.Security;

namespace Sangam.Timeline.Application.Posts.Commands.AddComment;

/// <summary>Comments on an approved post.</summary>
[RequiresPermission(PermissionKeys.TimelinePost)]
public sealed record AddCommentCommand(Guid PostId, string Body) : ICommand<CommentResponse>;

public sealed class AddCommentCommandValidator : AbstractValidator<AddCommentCommand>
{
    public AddCommentCommandValidator()
    {
        RuleFor(x => x.PostId).NotEmpty();
        RuleFor(x => x.Body).NotEmpty().MaximumLength(2000);
    }
}

public sealed class AddCommentCommandHandler(
    IPostRepository posts,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<AddCommentCommand, Result<CommentResponse>>
{
    public async Task<Result<CommentResponse>> Handle(
        AddCommentCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<CommentResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var post = await posts.GetByIdAsync(command.PostId, cancellationToken);

        if (post is null
            || (tenantContext.TenantId is { } tenantId && post.TenantId != tenantId))
        {
            return Result.Failure<CommentResponse>(
                Error.NotFound("Post.NotFound", "No such post in this Samaaj."));
        }

        var comment = post.Comment(memberId, command.Body, clock.UtcNow);

        if (comment is null)
        {
            // The post is not approved, so nobody can see it - which means the
            // only way to be commenting on it is to have guessed its id. Said
            // as "not found", because confirming it exists is the leak.
            return Result.Failure<CommentResponse>(
                Error.NotFound("Post.NotFound", "No such post in this Samaaj."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CommentResponse(
            comment.Id, comment.AuthorMemberId, comment.Body, comment.CreatedAt));
    }
}
