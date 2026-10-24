using FluentValidation;
using MediatR;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Application.Common;
using Sangam.Competition.Application.Security;

namespace Sangam.Competition.Application.Competitions.Commands.UpdateCompetition;

/// <summary>
/// Corrects a competition's details - the admin panel's edit popup. The same
/// fields and rules as creating one, and only while it is a Draft or taking
/// registrations: after that members have registered against these windows.
/// </summary>
[RequiresPermission(PermissionKeys.CompetitionManage)]
public sealed record UpdateCompetitionCommand(
    Guid CompetitionId,
    string Title,
    string? Description,
    string? CompetitionTypeId,
    string? LocalityId,
    DateTimeOffset RegistrationStartAt,
    DateTimeOffset RegistrationEndAt,
    string? Venue = null,
    string? CityId = null) : ICommand<CompetitionResponse>;

public sealed class UpdateCompetitionCommandValidator : AbstractValidator<UpdateCompetitionCommand>
{
    public UpdateCompetitionCommandValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.CompetitionTypeId).Must(BeNullOrAGuid).WithMessage("Competition type must be a valid id.");
        RuleFor(x => x.LocalityId).Must(BeNullOrAGuid).WithMessage("Locality must be a valid id.");
        RuleFor(x => x.CityId).Must(BeNullOrAGuid).WithMessage("City must be a valid id.");
        RuleFor(x => x.Venue).MaximumLength(300);

        RuleFor(x => x.RegistrationEndAt)
            .GreaterThan(x => x.RegistrationStartAt)
            .WithMessage("Registration cannot close before it opens.");
    }

    private static bool BeNullOrAGuid(string? value) => value is null || Guid.TryParse(value, out _);
}

public sealed class UpdateCompetitionCommandHandler(
    ICompetitionRepository competitions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateCompetitionCommand, Result<CompetitionResponse>>
{
    public async Task<Result<CompetitionResponse>> Handle(
        UpdateCompetitionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<CompetitionResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var competition = await competitions.GetByIdAsync(command.CompetitionId, cancellationToken);

        if (competition is null)
        {
            return Result.Failure<CompetitionResponse>(
                Error.NotFound("Competition.NotFound", "No such competition in this Samaaj."));
        }

        var updated = competition.Update(
            command.Title,
            command.Description,
            command.CompetitionTypeId is { } typeId ? Guid.Parse(typeId) : null,
            command.LocalityId is { } localityId ? Guid.Parse(localityId) : null,
            command.RegistrationStartAt,
            command.RegistrationEndAt,
            actorId,
            clock.UtcNow,
            command.Venue,
            command.CityId is { } cityId ? Guid.Parse(cityId) : null);

        if (!updated)
        {
            return Result.Failure<CompetitionResponse>(Error.Conflict(
                "Competition.NotEditable",
                "Registration has closed, so members have signed up to these details. It can no longer be edited."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(competition.ToResponse(actorId));
    }
}
