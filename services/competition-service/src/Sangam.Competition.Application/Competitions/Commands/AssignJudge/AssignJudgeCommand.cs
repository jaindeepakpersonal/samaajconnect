using FluentValidation;
using MediatR;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Application.Common;
using Sangam.Competition.Application.Security;

namespace Sangam.Competition.Application.Competitions.Commands.AssignJudge;

/// <summary>
/// Assigns or removes a judge, one command for both directions - the same
/// shape pathshala-service's own `AssignTeacherCommand` already uses.
/// </summary>
/// <remarks>
/// <paramref name="MemberId"/> is a wire `string`, not a `Guid` - a
/// Guid-typed field crashes ASP.NET Core's own JSON model binding with an
/// unhandled 400 the moment the value cannot be parsed, before
/// FluentValidation ever runs. Validated and parsed only in the handler,
/// the same defense-in-depth this platform already applies to every other
/// body-bound id (celebrity-voting-service's `NominateCandidateCommand`,
/// volunteer-groups-service's own election nomination).
/// </remarks>
[RequiresPermission(PermissionKeys.CompetitionManage)]
public sealed record AssignJudgeCommand(Guid CompetitionId, string MemberId, bool Assign)
    : ICommand<JudgeAssignmentResponse>;

public sealed class AssignJudgeCommandValidator : AbstractValidator<AssignJudgeCommand>
{
    public AssignJudgeCommandValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty();
        RuleFor(x => x.MemberId).Must(BeAGuid).WithMessage("Choose a member to assign.");
    }

    private static bool BeAGuid(string value) => Guid.TryParse(value, out _);
}

public sealed class AssignJudgeCommandHandler(
    ICompetitionRepository competitions,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<AssignJudgeCommand, Result<JudgeAssignmentResponse>>
{
    public async Task<Result<JudgeAssignmentResponse>> Handle(
        AssignJudgeCommand command, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByIdAsync(command.CompetitionId, cancellationToken);

        if (competition is null
            || (tenantContext.TenantId is { } tenantId && competition.TenantId != tenantId))
        {
            return Result.Failure<JudgeAssignmentResponse>(
                Error.NotFound("Competition.NotFound", "No such competition in this Samaaj."));
        }

        // Guaranteed parseable by AssignJudgeCommandValidator.BeAGuid.
        var memberId = Guid.Parse(command.MemberId);

        if (!competition.AssignJudge(memberId, command.Assign, clock.UtcNow))
        {
            return Result.Failure<JudgeAssignmentResponse>(Error.Conflict(
                "Judge.NoChange",
                command.Assign
                    ? "This member is already a judge for this competition."
                    : "This member is not a judge for this competition."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new JudgeAssignmentResponse(competition.Id, memberId, command.Assign));
    }
}
