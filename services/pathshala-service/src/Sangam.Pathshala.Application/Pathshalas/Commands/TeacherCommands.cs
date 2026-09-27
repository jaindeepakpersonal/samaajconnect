using FluentValidation;
using MediatR;
using Sangam.Pathshala.Application.Abstractions;
using Sangam.Pathshala.Application.Common;
using Sangam.Pathshala.Application.Security;
using Sangam.Pathshala.Domain.Pathshalas;

namespace Sangam.Pathshala.Application.Pathshalas.Commands;

// A Pathshala's teachers - who need not be members of the Samaaj. Adding,
// correcting and removing one is whoever runs the Pathshala: Pathshala.Manage,
// and an Area Admin for their own locality's only. Anybody else is answered
// "not found", as everywhere in this service.

/// <summary>What adding or correcting a teacher sends. <c>UserId</c> links their login, if they have one.</summary>
public interface ITeacherDetails
{
    string FullName { get; }
    string? Phone { get; }
    string? Email { get; }
    string? Qualification { get; }

    /// <summary>A string, not a Guid - see CreatePathshalaCommand. Null for a teacher with no login.</summary>
    string? UserId { get; }
}

internal sealed class TeacherDetailsValidator<T> : AbstractValidator<T> where T : ITeacherDetails
{
    public TeacherDetailsValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);

        RuleFor(x => x.Phone)
            .MaximumLength(20)
            .Matches(@"^\+?[0-9 ()-]{7,20}$")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Enter a phone number with digits only, optionally starting with +.");

        RuleFor(x => x.Email)
            .MaximumLength(200)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Qualification).MaximumLength(200);

        RuleFor(x => x.UserId)
            .Must(v => string.IsNullOrWhiteSpace(v) || Guid.TryParse(v, out _))
            .WithMessage("That login is not valid.");
    }
}

// ---- Add ---------------------------------------------------------------------

[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record AddTeacherCommand(
    Guid PathshalaId,
    string FullName,
    string? Phone,
    string? Email,
    string? Qualification,
    string? UserId) : ICommand<TeacherResponse>, ITeacherDetails;

public sealed class AddTeacherCommandValidator : AbstractValidator<AddTeacherCommand>
{
    public AddTeacherCommandValidator()
    {
        RuleFor(x => x.PathshalaId).NotEmpty();
        Include(new TeacherDetailsValidator<AddTeacherCommand>());
    }
}

public sealed class AddTeacherCommandHandler(
    IPathshalaRepository pathshalas,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IAreaScope areaScope,
    IDateTimeProvider clock)
    : IRequestHandler<AddTeacherCommand, Result<TeacherResponse>>
{
    public async Task<Result<TeacherResponse>> Handle(AddTeacherCommand command, CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByIdAsync(command.PathshalaId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId) || !pathshala.IsAdministeredBy(areaScope))
        {
            return Result.Failure<TeacherResponse>(PathshalaAccess.NoSuchPathshala);
        }

        var teacher = pathshala!.AddTeacher(
            command.FullName,
            command.Phone,
            command.Email,
            command.Qualification,
            TeacherInput.ParseUserId(command.UserId),
            currentUser.UserId ?? Guid.Empty,
            clock.UtcNow);

        if (teacher is null)
        {
            return Result.Failure<TeacherResponse>(TeacherInput.AccountTaken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(teacher.ToResponse(pathshala));
    }
}

// ---- Correct -----------------------------------------------------------------

[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record UpdateTeacherCommand(
    Guid PathshalaId,
    Guid TeacherId,
    string FullName,
    string? Phone,
    string? Email,
    string? Qualification,
    string? UserId) : ICommand<TeacherResponse>, ITeacherDetails;

public sealed class UpdateTeacherCommandValidator : AbstractValidator<UpdateTeacherCommand>
{
    public UpdateTeacherCommandValidator()
    {
        RuleFor(x => x.PathshalaId).NotEmpty();
        RuleFor(x => x.TeacherId).NotEmpty();
        Include(new TeacherDetailsValidator<UpdateTeacherCommand>());
    }
}

public sealed class UpdateTeacherCommandHandler(
    IPathshalaRepository pathshalas,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IAreaScope areaScope,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateTeacherCommand, Result<TeacherResponse>>
{
    public async Task<Result<TeacherResponse>> Handle(UpdateTeacherCommand command, CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByIdAsync(command.PathshalaId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId) || !pathshala.IsAdministeredBy(areaScope))
        {
            return Result.Failure<TeacherResponse>(PathshalaAccess.NoSuchPathshala);
        }

        var outcome = pathshala!.UpdateTeacher(
            command.TeacherId,
            command.FullName,
            command.Phone,
            command.Email,
            command.Qualification,
            TeacherInput.ParseUserId(command.UserId),
            currentUser.UserId ?? Guid.Empty,
            clock.UtcNow);

        switch (outcome)
        {
            case TeacherChange.NotFound:
                return Result.Failure<TeacherResponse>(TeacherInput.NoSuchTeacher);
            case TeacherChange.AccountTaken:
                return Result.Failure<TeacherResponse>(TeacherInput.AccountTaken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(pathshala.FindTeacher(command.TeacherId)!.ToResponse(pathshala));
    }
}

// ---- Remove ------------------------------------------------------------------

/// <summary>
/// Removes a teacher and every class assignment they held. The registers they
/// marked keep who marked them. Their login, if any, is not touched here - it
/// is an account in identity-tenant-service, and may teach elsewhere too.
/// </summary>
[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record RemoveTeacherCommand(Guid PathshalaId, Guid TeacherId) : ICommand<Guid>;

public sealed class RemoveTeacherCommandValidator : AbstractValidator<RemoveTeacherCommand>
{
    public RemoveTeacherCommandValidator()
    {
        RuleFor(x => x.PathshalaId).NotEmpty();
        RuleFor(x => x.TeacherId).NotEmpty();
    }
}

public sealed class RemoveTeacherCommandHandler(
    IPathshalaRepository pathshalas,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IAreaScope areaScope,
    IDateTimeProvider clock)
    : IRequestHandler<RemoveTeacherCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(RemoveTeacherCommand command, CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByIdAsync(command.PathshalaId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId) || !pathshala.IsAdministeredBy(areaScope))
        {
            return Result.Failure<Guid>(PathshalaAccess.NoSuchPathshala);
        }

        if (!pathshala!.RemoveTeacher(command.TeacherId, currentUser.UserId ?? Guid.Empty, clock.UtcNow))
        {
            return Result.Failure<Guid>(TeacherInput.NoSuchTeacher);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(command.TeacherId);
    }
}

internal static class TeacherInput
{
    public static readonly Error NoSuchTeacher =
        Error.NotFound("Teacher.NotFound", "This Pathshala has no such teacher.");

    public static readonly Error AccountTaken = Error.Conflict(
        "Teacher.AccountTaken", "That login already belongs to another teacher at this Pathshala.");

    // Guaranteed blank-or-parseable by TeacherDetailsValidator.
    public static Guid? ParseUserId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Guid.Parse(value);
}

internal static class TeacherMappings
{
    public static TeacherResponse ToResponse(this Teacher teacher, Domain.Pathshalas.Pathshala pathshala) =>
        new(teacher.Id,
            teacher.FullName,
            teacher.Phone,
            teacher.Email,
            teacher.Qualification,
            teacher.UserId,
            teacher.UserId is not null,
            [.. pathshala.Classes.Where(c => c.HasTeacher(teacher.Id)).Select(c => c.Id)],
            teacher.AddedAt);
}
