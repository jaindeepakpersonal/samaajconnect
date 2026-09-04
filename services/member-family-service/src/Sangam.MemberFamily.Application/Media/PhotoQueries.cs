using FluentValidation;
using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Media;

/// <summary>
/// The bytes of a member's photo, and what they are.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the "per-request authorization rather than an unguessable URL"
/// that SECURITY-CHECKLIST.md asks for</b>, and it is why the bytes are served
/// by the service that owns the profile rather than by a media service. The
/// rule for who may see a member's photo is the rule for who may see the member
/// — same Samaaj, <c>Members.Read</c> — and that rule already lives here. A
/// separate store would have had to be told it, or asked, or handed a signed
/// URL; keeping the bytes behind the aggregate that knows the answer means
/// there is one rule rather than two that can disagree.
/// </para>
/// <para>
/// A photo carries no <c>PrivacyLevel</c> of its own, matching the field it
/// replaced. Being in the directory is what makes a member visible, and
/// `IsListedInDirectory` governs that; a member who wants no picture shown
/// removes it.
/// </para>
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetMemberPhotoQuery(Guid MemberId) : IQuery<PhotoContent>;

public sealed class GetMemberPhotoQueryValidator : AbstractValidator<GetMemberPhotoQuery>
{
    public GetMemberPhotoQueryValidator() => RuleFor(x => x.MemberId).NotEmpty();
}

public sealed class GetMemberPhotoQueryHandler(
    IMemberProfileRepository profiles,
    IImageStore images)
    : IRequestHandler<GetMemberPhotoQuery, Result<PhotoContent>>
{
    public async Task<Result<PhotoContent>> Handle(
        GetMemberPhotoQuery query,
        CancellationToken cancellationToken)
    {
        var profile = await profiles.GetByIdAsync(query.MemberId, cancellationToken);

        // No such member and no photo answer the same way. Telling a caller
        // that a member exists but has no picture is a fact about somebody they
        // may not be able to see at all, and the endpoint has no use for the
        // distinction.
        if (profile?.PhotoImageId is not { } imageId)
        {
            return Result.Failure<PhotoContent>(
                Error.NotFound("Member.NoPhoto", "No photo for that member."));
        }

        var image = await images.GetAsync(imageId, cancellationToken);

        if (image is null)
        {
            return Result.Failure<PhotoContent>(
                Error.NotFound("Member.NoPhoto", "No photo for that member."));
        }

        return Result.Success(new PhotoContent(
            image.Bytes, image.ContentType, image.ContentHash, image.UploadedAt));
    }
}

/// <summary>
/// The bytes of a child's photo.
/// </summary>
/// <remarks>
/// The authorization is the family's, not the Samaaj's, and that is the whole
/// difference from a member's photo. A child's record is visible to the
/// household it belongs to; the Samaaj directory has no business in it. This
/// resolves the caller's own family and refuses anything outside it — the same
/// check <c>ListFamilyChildrenQuery</c> makes, which is why the answer to
/// "somebody else's child" is 404 rather than 403: a 403 would confirm the id
/// names a real child.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetChildPhotoQuery(Guid ChildId) : IQuery<PhotoContent>;

public sealed class GetChildPhotoQueryValidator : AbstractValidator<GetChildPhotoQuery>
{
    public GetChildPhotoQueryValidator() => RuleFor(x => x.ChildId).NotEmpty();
}

public sealed class GetChildPhotoQueryHandler(
    IChildRepository children,
    IFamilyRepository families,
    IImageStore images,
    ICurrentUser currentUser)
    : IRequestHandler<GetChildPhotoQuery, Result<PhotoContent>>
{
    public async Task<Result<PhotoContent>> Handle(
        GetChildPhotoQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<PhotoContent>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var child = await children.GetByIdAsync(query.ChildId, cancellationToken);
        var family = await families.GetForMemberAsync(memberId, cancellationToken);

        if (child is null || family is null || child.FamilyId != family.Id)
        {
            return Result.Failure<PhotoContent>(
                Error.NotFound("Child.NoPhoto", "No photo for that child."));
        }

        if (child.PhotoImageId is not { } imageId)
        {
            return Result.Failure<PhotoContent>(
                Error.NotFound("Child.NoPhoto", "No photo for that child."));
        }

        var image = await images.GetAsync(imageId, cancellationToken);

        if (image is null)
        {
            return Result.Failure<PhotoContent>(
                Error.NotFound("Child.NoPhoto", "No photo for that child."));
        }

        return Result.Success(new PhotoContent(
            image.Bytes, image.ContentType, image.ContentHash, image.UploadedAt));
    }
}

/// <summary>
/// Bytes ready to be written to a response, with what a browser needs to cache
/// them.
/// </summary>
/// <remarks>
/// <paramref name="ContentType"/> was sniffed from the bytes when they were
/// stored, never taken from the upload's header — so the type a viewer's
/// browser acts on is one the platform derived. <paramref name="ETag"/> is the
/// stored SHA-256, which is what lets a directory page of a hundred photos cost
/// a hundred 304s on the second visit.
/// </remarks>
public sealed record PhotoContent(
    byte[] Bytes,
    string ContentType,
    string ETag,
    DateTimeOffset UploadedAt);
