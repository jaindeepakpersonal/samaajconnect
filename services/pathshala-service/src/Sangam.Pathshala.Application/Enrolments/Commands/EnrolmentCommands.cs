using FluentValidation;
using MediatR;
using Sangam.Pathshala.Application.Abstractions;
using Sangam.Pathshala.Application.Common;
using Sangam.Pathshala.Application.Pathshalas;
using Sangam.Pathshala.Application.Security;
using Sangam.Pathshala.Domain.Enrolments;

namespace Sangam.Pathshala.Application.Enrolments.Commands;

// ---- A parent asks -----------------------------------------------------------

/// <summary>
/// Asks for a place at a Pathshala for a child.
/// </summary>
/// <remarks>
/// Gated on <c>Members.Read</c>, which every signed-in member holds, rather than
/// on FamilyHead. A household's head is who the wireframe expects, but the role
/// is earned by creating a family and a Samaaj with one parent registered as an
/// ordinary member would otherwise be unable to enrol their child at all.
///
/// This service cannot check that the child is the caller's - that is
/// member-family-service's fact. The placement step is where somebody who knows
/// the family looks, and an unplaced request grants access to nothing, because
/// no attendance or result exists to read.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record RequestEnrolmentCommand(Guid PathshalaId, Guid ChildProfileId)
    : ICommand<EnrolmentResponse>;

public sealed class RequestEnrolmentCommandValidator : AbstractValidator<RequestEnrolmentCommand>
{
    public RequestEnrolmentCommandValidator()
    {
        RuleFor(x => x.PathshalaId).NotEmpty();
        RuleFor(x => x.ChildProfileId).NotEmpty();
    }
}

public sealed class RequestEnrolmentCommandHandler(
    IPathshalaRepository pathshalas,
    IEnrolmentRepository enrolments,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RequestEnrolmentCommand, Result<EnrolmentResponse>>
{
    public async Task<Result<EnrolmentResponse>> Handle(
        RequestEnrolmentCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<EnrolmentResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var pathshala = await pathshalas.GetByIdAsync(command.PathshalaId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId))
        {
            return Result.Failure<EnrolmentResponse>(PathshalaAccess.NoSuchPathshala);
        }

        if (!pathshala!.AcceptsEnrolments)
        {
            return Result.Failure<EnrolmentResponse>(Error.Conflict(
                "Pathshala.NotEnrolling",
                "This Pathshala is not taking enrolments - it has no current session, or it "
                + "has stopped operating."));
        }

        // The courtesy check behind the unique index on
        // (PathshalaId, ChildProfileId). A parent who submits the form twice
        // gets their existing request back rather than a database error.
        var existing = await enrolments.FindForChildAsync(
            pathshala.Id, command.ChildProfileId, cancellationToken);

        if (existing is not null)
        {
            return Result.Success(existing.ToResponse());
        }

        var enrolment = StudentEnrolment.Request(
            pathshala.TenantId, pathshala.Id, command.ChildProfileId, memberId, clock.UtcNow);

        enrolments.Add(enrolment);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(enrolment.ToResponse());
    }
}

// ---- The Pathshala decides ---------------------------------------------------

/// <summary>
/// Places a requested child in a class, or turns the request down.
/// </summary>
/// <remarks>
/// The step that makes enrolment safe as well as correct. Somebody at the
/// Pathshala picks the class - which the parent could not have done - and in
/// doing so confirms the child is who the request says.
/// </remarks>
[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record PlaceStudentCommand(Guid EnrolmentId, Guid? ClassId, bool Place)
    : ICommand<EnrolmentResponse>;

public sealed class PlaceStudentCommandValidator : AbstractValidator<PlaceStudentCommand>
{
    public PlaceStudentCommandValidator()
    {
        RuleFor(x => x.EnrolmentId).NotEmpty();

        RuleFor(x => x.ClassId)
            .NotEmpty()
            .When(x => x.Place)
            .WithMessage("A class is required to place a student.");
    }
}

public sealed class PlaceStudentCommandHandler(
    IPathshalaRepository pathshalas,
    IEnrolmentRepository enrolments,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<PlaceStudentCommand, Result<EnrolmentResponse>>
{
    public async Task<Result<EnrolmentResponse>> Handle(
        PlaceStudentCommand command, CancellationToken cancellationToken)
    {
        var enrolment = await enrolments.GetByIdAsync(command.EnrolmentId, cancellationToken);

        if (!enrolment.IsInTenant(tenantContext.TenantId))
        {
            return Result.Failure<EnrolmentResponse>(PathshalaAccess.NoSuchEnrolment);
        }

        if (!command.Place)
        {
            if (!enrolment!.Decline(clock.UtcNow))
            {
                return Result.Failure<EnrolmentResponse>(Error.Conflict(
                    "Enrolment.NotWaiting", "This request has already been decided."));
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(enrolment.ToResponse());
        }

        var pathshala = await pathshalas.GetByIdAsync(enrolment!.PathshalaId, cancellationToken);
        var pathshalaClass = pathshala?.FindClass(command.ClassId!.Value);

        if (pathshalaClass is null)
        {
            return Result.Failure<EnrolmentResponse>(Error.NotFound(
                "Class.NotFound", "This Pathshala has no class with that id."));
        }

        var session = pathshala!.FindSession(pathshalaClass.SessionId);

        if (session is null || !session.IsCurrent)
        {
            // Placing into a closed session produces a child who looks enrolled
            // and appears on no current register.
            return Result.Failure<EnrolmentResponse>(Error.Conflict(
                "Class.NotCurrent", "That class belongs to a session that is not the current one."));
        }

        if (!enrolment.PlaceIn(pathshalaClass.Id, session.Id, clock.UtcNow))
        {
            return Result.Failure<EnrolmentResponse>(Error.Conflict(
                "Enrolment.NotWaiting", "This request has already been decided."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(enrolment.ToResponse(pathshalaClass.Name, session.Label));
    }
}

/// <summary>Takes a placed student off the roll. Their records stay.</summary>
[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record WithdrawStudentCommand(Guid EnrolmentId) : ICommand<EnrolmentResponse>;

public sealed class WithdrawStudentCommandValidator : AbstractValidator<WithdrawStudentCommand>
{
    public WithdrawStudentCommandValidator() => RuleFor(x => x.EnrolmentId).NotEmpty();
}

public sealed class WithdrawStudentCommandHandler(
    IEnrolmentRepository enrolments,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<WithdrawStudentCommand, Result<EnrolmentResponse>>
{
    public async Task<Result<EnrolmentResponse>> Handle(
        WithdrawStudentCommand command, CancellationToken cancellationToken)
    {
        var enrolment = await enrolments.GetByIdAsync(command.EnrolmentId, cancellationToken);

        if (!enrolment.IsInTenant(tenantContext.TenantId))
        {
            return Result.Failure<EnrolmentResponse>(PathshalaAccess.NoSuchEnrolment);
        }

        if (!enrolment!.Withdraw(clock.UtcNow))
        {
            return Result.Failure<EnrolmentResponse>(Error.Conflict(
                "Enrolment.NotOnRoll", "This student is not currently on the roll."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(enrolment.ToResponse());
    }
}
