using FluentValidation;
using Sangam.Timeline.Application.Common;
using Sangam.Timeline.Application.Security;

namespace Sangam.Timeline.Application.Posts.Commands.CreatePost;

/// <summary>
/// Writes a post to the Samaaj's timeline.
/// </summary>
/// <remarks>
/// One command for both post types, because the difference is entirely in what
/// the caller is allowed to do, not in what they submit. Asking for an
/// announcement without holding Timeline.Moderate is refused in the handler;
/// everyone else gets a member post, which goes to the queue.
///
/// There is no media. The wireframe has an "Attach Photo" button and the data
/// model has PostMedia with a ScanStatus, and both are honest about what is
/// needed: SECURITY-CHECKLIST.md requires uploaded files to be size- and
/// type-restricted and virus-scanned before being served to anyone. The
/// platform has no file storage yet, and accepting a link to somebody else's
/// host would put an unscanned image in front of the whole Samaaj and send
/// every viewer's address to that host. Media arrives with storage.
/// </remarks>
// The permission alone, with no [RequiresRoles] beside it. Every command here
// is open to whoever holds the permission, which is precisely what the
// permission is for - and a role list would be a second, longer answer to the
// same question that has to be kept in step with AuthorizationCatalog by hand.
// Members hold Timeline.Post; moderators hold it too, because everyone with a
// login is a Member first.
[RequiresPermission(PermissionKeys.TimelinePost)]
public sealed record CreatePostCommand(string Title, string Body, bool AsAnnouncement = false)
    : ICommand<PostResponse>;

public sealed class CreatePostCommandValidator : AbstractValidator<CreatePostCommand>
{
    public CreatePostCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);

        // Long enough for a real notice about a programme; short enough that
        // the timeline stays a timeline.
        RuleFor(x => x.Body).NotEmpty().MaximumLength(5000);
    }
}
