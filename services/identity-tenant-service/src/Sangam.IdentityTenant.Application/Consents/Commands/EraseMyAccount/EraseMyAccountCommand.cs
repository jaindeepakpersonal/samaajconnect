using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Consents.Commands.EraseMyAccount;

/// <summary>
/// Erases the caller's account and, through the event it raises, their data
/// across the platform (DPDP section 12).
/// </summary>
/// <remarks>
/// No admin approval. Section 12 gives the Data Principal a right, not a
/// request for permission, so gating it behind a Samaaj admin would be the
/// wrong shape - unlike adult-child conversion, where an admin is deciding
/// whether to *create* something.
///
/// The password is required instead. It proves the person at the keyboard is
/// the account holder, which is the identity verification a Fiduciary needs
/// before acting on an irreversible request, and it makes the action
/// deliberate rather than a mis-click.
/// </remarks>
[RequiresRoles(
    Roles.Member,
    Roles.FamilyHead,
    Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher,
    Roles.PathshalaStudent,
    Roles.ContentModerator,
    Roles.BoliManager,
    Roles.SamaajAdmin)]
public sealed record EraseMyAccountCommand(string Password) : ICommand<EraseMyAccountResponse>;

/// <summary>
/// What survives, said plainly. A member who erases their account and is told
/// only "done" has no way to know an audit record remains.
/// </summary>
public sealed record EraseMyAccountResponse(
    Guid UserId,
    DateTimeOffset ErasedAt,
    IReadOnlyList<string> WhatWasErased,
    IReadOnlyList<string> WhatIsKeptAndWhy);

public sealed class EraseMyAccountCommandValidator : AbstractValidator<EraseMyAccountCommand>
{
    public EraseMyAccountCommandValidator()
    {
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}

public sealed class EraseMyAccountCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ISessionService sessions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ILogger<EraseMyAccountCommandHandler> logger)
    : IRequestHandler<EraseMyAccountCommand, Result<EraseMyAccountResponse>>
{
    public async Task<Result<EraseMyAccountResponse>> Handle(
        EraseMyAccountCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<EraseMyAccountResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var user = await users.GetByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<EraseMyAccountResponse>(
                Error.NotFound("User.NotFound", "This account no longer exists."));
        }

        if (!passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            return Result.Failure<EraseMyAccountResponse>(Error.Unauthorized(
                "Auth.InvalidCredentials",
                "That password is not correct. Erasing an account cannot be undone, so we ask "
                + "for it first."));
        }

        // A Super Admin erasing themselves would leave a platform nobody can
        // administer, and there is no second Super Admin to notice.
        if (currentUser.IsInRole(Roles.SuperAdmin))
        {
            return Result.Failure<EraseMyAccountResponse>(Error.Conflict(
                "Erasure.PlatformAdmin",
                "A platform administrator account cannot be erased through this route."));
        }

        user.Erase(clock.UtcNow);

        // Every session for this account ends now. The access token cannot be
        // withdrawn and outlives this by its remaining lifetime, but nothing
        // can renew it - so the residual window is minutes rather than
        // "until somebody notices".
        await sessions.EndAllForUserAsync(
            user.Id, SessionEndReason.AccountErased, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Logged with the id only. A log line naming who erased themselves
        // would preserve exactly what was just erased.
        logger.LogWarning("Account {UserId} erased at the member's request", user.Id);

        return Result.Success(new EraseMyAccountResponse(
            user.Id,
            clock.UtcNow,
            [
                "Your login, name and contact details.",
                "Your roles and what you were allowed to do.",
                "Your profile, family links and any children's records you held.",
                "Your notifications.",
            ],
            [
                "The record that actions were taken on this platform, and when, with your "
                + "identity removed from it. A Samaaj has to be able to account for decisions "
                + "made about its members, and a record that can be erased is not a record.",
                "The consent you gave and withdrew, without your details attached, because a "
                + "Samaaj must be able to show what it relied on.",
            ]));
    }
}
