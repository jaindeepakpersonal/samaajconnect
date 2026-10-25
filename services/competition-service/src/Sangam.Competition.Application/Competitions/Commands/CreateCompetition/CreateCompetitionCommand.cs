using FluentValidation;
using MediatR;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Application.Common;
using Sangam.Competition.Application.Security;
using Sangam.Competition.Domain.Competitions;
using CompetitionAggregate = Sangam.Competition.Domain.Competitions.Competition;

namespace Sangam.Competition.Application.Competitions.Commands.CreateCompetition;

/// <summary>Sets a competition up. It stays a draft until somebody opens registration.</summary>
[RequiresPermission(PermissionKeys.CompetitionManage)]
public sealed record CreateCompetitionCommand(
    string Title,
    string? Description,
    string? CompetitionTypeId,
    DateTimeOffset RegistrationStartAt,
    DateTimeOffset RegistrationEndAt,

    /// <summary>A string, validated as a Guid; null for the whole Samaaj.</summary>
    string? LocalityId = null,
    string? Venue = null,
    string? CityId = null,
    /// <summary>The format (2026-10-25) - see `CompetitionFormat`. Each optional; left out, it is the default.</summary>
    int? RoundCount = null,
    int? MaxMarks = null,
    bool? AuditionRequired = null,
    DateOnly? AuditionDate = null,
    int? FinalistCount = null) : ICommand<CompetitionResponse>, IHasCompetitionFormat;

public sealed class CreateCompetitionCommandValidator : AbstractValidator<CreateCompetitionCommand>
{
    public CreateCompetitionCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);

        RuleFor(x => x.CompetitionTypeId)
            .Must(BeNullOrAGuid)
            .WithMessage("Competition type must be a valid id.");

        RuleFor(x => x.LocalityId)
            .Must(BeNullOrAGuid)
            .WithMessage("Locality must be a valid id.");

        RuleFor(x => x.CityId)
            .Must(BeNullOrAGuid)
            .WithMessage("City must be a valid id.");

        RuleFor(x => x.Venue).MaximumLength(300);

        RuleFor(x => x.RegistrationEndAt)
            .GreaterThan(x => x.RegistrationStartAt)
            .WithMessage("Registration cannot close before it opens.");

        Include(new CompetitionFormatValidator());
    }

    private static bool BeNullOrAGuid(string? value) =>
        value is null || Guid.TryParse(value, out _);
}

public sealed class CreateCompetitionCommandHandler(
    ICompetitionRepository competitions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<CreateCompetitionCommand, Result<CompetitionResponse>>
{
    public async Task<Result<CompetitionResponse>> Handle(
        CreateCompetitionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<CompetitionResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            return Result.Failure<CompetitionResponse>(Error.Forbidden(
                "Competition.NoSamaaj", "Select a Samaaj before creating a competition in it."));
        }

        var competition = CompetitionAggregate.Create(
            tenantId,
            command.Title,
            command.Description,
            command.CompetitionTypeId is { } typeId ? Guid.Parse(typeId) : null,
            command.RegistrationStartAt,
            command.RegistrationEndAt,
            actorId,
            clock.UtcNow,
            command.LocalityId is { } localityId ? Guid.Parse(localityId) : null,
            command.Venue,
            command.CityId is { } cityId ? Guid.Parse(cityId) : null,
            command.ToFormat(CompetitionFormat.Default));

        competitions.Add(competition);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(competition.ToResponse(actorId));
    }
}
