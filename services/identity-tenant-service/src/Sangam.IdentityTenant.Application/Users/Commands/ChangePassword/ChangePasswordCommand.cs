using FluentValidation;
using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.ChangePassword;

/// <summary>
/// Sets a new password for the signed-in account, given the current one.
/// </summary>
/// <remarks>
/// This did not exist anywhere on the platform until now - not for a member,
/// not for a Samaaj Admin, not for the bootstrap Super Admin. <see cref="User"/>
/// had exactly two places that ever wrote <see cref="User.PasswordHash"/> after
/// construction: nowhere, and <c>Activate()</c>, which only runs once, for a
/// <see cref="UserStatus.PendingActivation"/> account redeeming a code. An
/// account that already has a working password had no way to replace it.
///
/// The current password is verified inline rather than through
/// <see cref="IStepUpAuthentication"/>, though the shape is the same - read the
/// caller past the tenant filter, check the lockout, verify, count a failure
/// through the same recorder a failed login uses - and it returns the same
/// <see cref="IStepUpAuthentication.StepUpFailedCode"/> for anything that
/// switches on it. What differs is the message: that service's is written for
/// an irreversible action ("...cannot be undone, so we ask for it first"),
/// which is false for a password change - you can always change it back - and
/// this platform's own conventions are explicit that a security message about
/// something that cannot happen dilutes the ones that matter.
///
/// Every other session for the account ends the moment the new password is
/// set, through <see cref="ISessionService.EndAllForUserAsync"/> with
/// <see cref="SessionEndReason.PasswordChanged"/> - a reason that has existed
/// since the first migration with nothing ever raising it. A stolen but
/// still-valid refresh token is worth nothing once its owner changes their
/// password; leaving it live would mean the whole point of changing a password
/// only bites once that token's own fifteen-day life runs out.
/// </remarks>
[RequiresRoles(
    Roles.SuperAdmin,
    Roles.SamaajAdmin,
    Roles.Member,
    Roles.FamilyHead,
    Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher,
    Roles.PathshalaStudent,
    Roles.ContentModerator,
    Roles.BoliManager)]
public sealed record ChangePasswordCommand(
    string CurrentPassword, string NewPassword) : ICommand<ChangePasswordResponse>;

public sealed record ChangePasswordResponse(Guid UserId, DateTimeOffset ChangedAt);

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(256);

        // Same rule as registration and activation - one password policy,
        // never three that can drift.
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(10).MaximumLength(256);

        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("Choose a password that is different from your current one.")
            .When(x => !string.IsNullOrEmpty(x.CurrentPassword) && !string.IsNullOrEmpty(x.NewPassword));
    }
}

public sealed class ChangePasswordCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IFailedLoginRecorder failedLoginRecorder,
    ISessionService sessions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ChangePasswordCommand, Result<ChangePasswordResponse>>
{
    public async Task<Result<ChangePasswordResponse>> Handle(
        ChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<ChangePasswordResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        // GetSelfAsync, not GetByIdAsync: a Super Admin's own account lives at
        // User.PlatformTenantId, outside whatever Samaaj they may be acting on,
        // so the tenant-filtered lookup would find nothing for the one caller
        // who most needs to be able to reach this.
        var user = await users.GetSelfAsync(userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<ChangePasswordResponse>(
                Error.NotFound("User.NotFound", "This account no longer exists."));
        }

        if (user.Status != UserStatus.Active)
        {
            return Result.Failure<ChangePasswordResponse>(Error.Conflict(
                "User.NotActive", "This account cannot change its password right now."));
        }

        if (user.IsLockedOut(clock.UtcNow))
        {
            return Result.Failure<ChangePasswordResponse>(Error.Forbidden(
                "Auth.LockedOut", "Too many failed attempts. Try again in a few minutes."));
        }

        if (!passwordHasher.Verify(command.CurrentPassword, user.PasswordHash))
        {
            await failedLoginRecorder.RecordAsync(user.Id, cancellationToken);

            return Result.Failure<ChangePasswordResponse>(Error.Forbidden(
                IStepUpAuthentication.StepUpFailedCode, "Your current password is not correct."));
        }

        var now = clock.UtcNow;

        user.ChangePassword(passwordHasher.Hash(command.NewPassword), now);

        // Every other session ends here. The access token this very request
        // carries cannot be withdrawn and outlives this by its remaining
        // fifteen minutes at most, but nothing can renew it afterwards.
        await sessions.EndAllForUserAsync(user.Id, SessionEndReason.PasswordChanged, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new ChangePasswordResponse(user.Id, now));
    }
}
