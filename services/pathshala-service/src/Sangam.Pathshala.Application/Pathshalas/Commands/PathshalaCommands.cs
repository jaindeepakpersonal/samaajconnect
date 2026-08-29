using FluentValidation;
using MediatR;
using Sangam.Pathshala.Application.Abstractions;
using Sangam.Pathshala.Application.Common;
using Sangam.Pathshala.Application.Security;

namespace Sangam.Pathshala.Application.Pathshalas.Commands;

// ---- Create the master record ------------------------------------------------

/// <summary>
/// Creates a Pathshala.
/// </summary>
/// <remarks>
/// The one Pathshala act reserved to the platform (DATA-MODEL.md section 9), so
/// it carries the SuperAdmin role <i>and</i> the permission. Everything after
/// this - sessions, classes, teachers, placements - is the Samaaj's to run and
/// carries the permission alone, which a Samaaj Admin holds.
///
/// Withholding the permission from Samaaj Admins would have been the other way
/// to reserve this, and it was the wrong way: it would have made every other
/// Pathshala operation reachable by nobody but the platform operator.
/// </remarks>
[RequiresRoles(Roles.SuperAdmin)]
[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record CreatePathshalaCommand(
    string Name, string? Address, string? ContactPerson) : ICommand<PathshalaResponse>;

public sealed class CreatePathshalaCommandValidator : AbstractValidator<CreatePathshalaCommand>
{
    public CreatePathshalaCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.ContactPerson).MaximumLength(200);
    }
}

public sealed class CreatePathshalaCommandHandler(
    IPathshalaRepository pathshalas,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<CreatePathshalaCommand, Result<PathshalaResponse>>
{
    public async Task<Result<PathshalaResponse>> Handle(
        CreatePathshalaCommand command, CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            // A Super Admin acts on a Samaaj by overriding into one. Without
            // that the row would land at Guid.Empty and belong to nobody.
            return Result.Failure<PathshalaResponse>(Error.Forbidden(
                "Pathshala.NoSamaaj", "Select a Samaaj before creating a Pathshala in it."));
        }

        var pathshala = Domain.Pathshalas.Pathshala.Create(
            tenantId, command.Name, command.Address, command.ContactPerson, clock.UtcNow);

        pathshalas.Add(pathshala);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(pathshala.ToResponse());
    }
}

// ---- Academic sessions -------------------------------------------------------

/// <summary>Opens an academic session and makes it the current one.</summary>
[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record OpenSessionCommand(
    Guid PathshalaId, string Label, DateOnly StartDate, DateOnly EndDate)
    : ICommand<PathshalaDetailResponse>;

public sealed class OpenSessionCommandValidator : AbstractValidator<OpenSessionCommand>
{
    public OpenSessionCommandValidator()
    {
        RuleFor(x => x.PathshalaId).NotEmpty();
        RuleFor(x => x.Label).NotEmpty().MaximumLength(50);

        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate)
            .WithMessage("A session cannot end before it starts.");
    }
}

public sealed class OpenSessionCommandHandler(
    IPathshalaRepository pathshalas,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<OpenSessionCommand, Result<PathshalaDetailResponse>>
{
    public async Task<Result<PathshalaDetailResponse>> Handle(
        OpenSessionCommand command, CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByIdAsync(command.PathshalaId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId))
        {
            return Result.Failure<PathshalaDetailResponse>(PathshalaAccess.NoSuchPathshala);
        }

        if (pathshala!.Sessions.Any(s => string.Equals(
                s.Label, command.Label.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            // Two sessions called "2026-27" makes every record that names one
            // ambiguous, and there is no way to tell them apart afterwards.
            return Result.Failure<PathshalaDetailResponse>(Error.Conflict(
                "Session.Duplicate", "This Pathshala already has a session with that label."));
        }

        pathshala.OpenSession(command.Label, command.StartDate, command.EndDate, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(pathshala.ToDetail(new Dictionary<Guid, int>()));
    }
}

// ---- Classes -----------------------------------------------------------------

/// <summary>Adds a class to a session.</summary>
[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record CreateClassCommand(
    Guid PathshalaId, Guid SessionId, string Name, string? RoomLabel) : ICommand<ClassResponse>;

public sealed class CreateClassCommandValidator : AbstractValidator<CreateClassCommand>
{
    public CreateClassCommandValidator()
    {
        RuleFor(x => x.PathshalaId).NotEmpty();
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoomLabel).MaximumLength(50);
    }
}

public sealed class CreateClassCommandHandler(
    IPathshalaRepository pathshalas, IUnitOfWork unitOfWork, ITenantContext tenantContext)
    : IRequestHandler<CreateClassCommand, Result<ClassResponse>>
{
    public async Task<Result<ClassResponse>> Handle(
        CreateClassCommand command, CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByIdAsync(command.PathshalaId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId))
        {
            return Result.Failure<ClassResponse>(PathshalaAccess.NoSuchPathshala);
        }

        var created = pathshala!.AddClass(command.SessionId, command.Name, command.RoomLabel);

        if (created is null)
        {
            return Result.Failure<ClassResponse>(Error.NotFound(
                "Session.NotFound", "This Pathshala has no session with that id."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(created.ToResponse(
            pathshala.FindSession(command.SessionId)!.Label, studentCount: 0));
    }
}

/// <summary>Adds a weekly slot to a class's timetable.</summary>
[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record AddClassSlotCommand(
    Guid ClassId, string DayOfWeek, TimeOnly StartTime, TimeOnly EndTime) : ICommand<ClassResponse>;

public sealed class AddClassSlotCommandValidator : AbstractValidator<AddClassSlotCommand>
{
    public AddClassSlotCommandValidator()
    {
        RuleFor(x => x.ClassId).NotEmpty();

        RuleFor(x => x.DayOfWeek)
            .NotEmpty()
            .Must(d => Enum.TryParse<DayOfWeek>(d, ignoreCase: true, out _))
            .WithMessage(
                $"Day must be one of: {string.Join(", ", Enum.GetNames<DayOfWeek>())}.");
    }
}

public sealed class AddClassSlotCommandHandler(
    IPathshalaRepository pathshalas, IUnitOfWork unitOfWork, ITenantContext tenantContext)
    : IRequestHandler<AddClassSlotCommand, Result<ClassResponse>>
{
    public async Task<Result<ClassResponse>> Handle(
        AddClassSlotCommand command, CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByClassIdAsync(command.ClassId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId))
        {
            return Result.Failure<ClassResponse>(PathshalaAccess.NoSuchClass);
        }

        var pathshalaClass = pathshala!.FindClass(command.ClassId)!;

        var added = pathshalaClass.AddSlot(
            Enum.Parse<DayOfWeek>(command.DayOfWeek, ignoreCase: true),
            command.StartTime,
            command.EndTime);

        if (!added)
        {
            return Result.Failure<ClassResponse>(Error.Conflict(
                "Class.SlotClash",
                "That slot ends before it starts, or overlaps one this class already has."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(pathshalaClass.ToResponse(
            pathshala.FindSession(pathshalaClass.SessionId)?.Label ?? string.Empty, 0));
    }
}

/// <summary>Assigns or removes a teacher on a class.</summary>
/// <remarks>
/// One command for both directions because they are the same decision, taken by
/// the same person, against the same row - and two would be two copies of the
/// tenant and Pathshala checks above.
/// </remarks>
[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record AssignTeacherCommand(Guid ClassId, Guid TeacherMemberId, bool Assign)
    : ICommand<ClassResponse>;

public sealed class AssignTeacherCommandValidator : AbstractValidator<AssignTeacherCommand>
{
    public AssignTeacherCommandValidator()
    {
        RuleFor(x => x.ClassId).NotEmpty();
        RuleFor(x => x.TeacherMemberId).NotEmpty();
    }
}

public sealed class AssignTeacherCommandHandler(
    IPathshalaRepository pathshalas,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<AssignTeacherCommand, Result<ClassResponse>>
{
    public async Task<Result<ClassResponse>> Handle(
        AssignTeacherCommand command, CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByClassIdAsync(command.ClassId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId))
        {
            return Result.Failure<ClassResponse>(PathshalaAccess.NoSuchClass);
        }

        var pathshalaClass = pathshala!.FindClass(command.ClassId)!;

        // A no-op either way is success. Assigning a teacher who already
        // teaches the class, or removing one who does not, is not an error -
        // the caller wanted a state and that is the state.
        var changed = command.Assign
            ? pathshalaClass.AssignTeacher(command.TeacherMemberId, clock.UtcNow)
            : pathshalaClass.RemoveTeacher(command.TeacherMemberId);

        if (changed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(pathshalaClass.ToResponse(
            pathshala.FindSession(pathshalaClass.SessionId)?.Label ?? string.Empty, 0));
    }
}

/// <summary>Stops a Pathshala operating. It takes no further enrolments.</summary>
[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record DeactivatePathshalaCommand(Guid PathshalaId) : ICommand<PathshalaResponse>;

public sealed class DeactivatePathshalaCommandValidator
    : AbstractValidator<DeactivatePathshalaCommand>
{
    public DeactivatePathshalaCommandValidator() => RuleFor(x => x.PathshalaId).NotEmpty();
}

public sealed class DeactivatePathshalaCommandHandler(
    IPathshalaRepository pathshalas,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<DeactivatePathshalaCommand, Result<PathshalaResponse>>
{
    public async Task<Result<PathshalaResponse>> Handle(
        DeactivatePathshalaCommand command, CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByIdAsync(command.PathshalaId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId))
        {
            return Result.Failure<PathshalaResponse>(PathshalaAccess.NoSuchPathshala);
        }

        pathshala!.Deactivate(clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(pathshala.ToResponse());
    }
}
