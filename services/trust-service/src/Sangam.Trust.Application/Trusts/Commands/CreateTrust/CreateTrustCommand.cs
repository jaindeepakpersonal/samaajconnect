using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Trusts;

namespace Sangam.Trust.Application.Trusts.Commands.CreateTrust;

/// <summary>Sets a Trust up for a locality. Exactly one per (Samaaj, Locality).</summary>
/// <remarks>
/// <paramref name="LocalityId"/> is a string, not a <see cref="Guid"/> - a
/// Guid-typed wire field crashes ASP.NET Core's own JSON model binding with an
/// unhandled 400 the moment the value cannot be parsed, before FluentValidation
/// ever runs (the same defense-in-depth panch-service's
/// <c>NominateCandidateCommand</c> uses for a body-bound id).
/// </remarks>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record CreateTrustCommand(string LocalityId, string Name) : ICommand<TrustResponse>;

public sealed class CreateTrustCommandValidator : AbstractValidator<CreateTrustCommand>
{
    public CreateTrustCommandValidator()
    {
        RuleFor(x => x.LocalityId).Must(BeAGuid).WithMessage("Choose a locality.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }

    private static bool BeAGuid(string value) => Guid.TryParse(value, out _);
}

public sealed class CreateTrustCommandHandler(
    ITrustRepository trusts,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<CreateTrustCommand, Result<TrustResponse>>
{
    public async Task<Result<TrustResponse>> Handle(
        CreateTrustCommand command,
        CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            return Result.Failure<TrustResponse>(Error.Forbidden(
                "Trust.NoSamaaj", "Select a Samaaj before creating a Trust in it."));
        }

        // Guaranteed parseable by CreateTrustCommandValidator.BeAGuid.
        var localityId = Guid.Parse(command.LocalityId);

        var existing = await trusts.FindByLocalityAsync(localityId, cancellationToken);

        if (existing is not null)
        {
            return Result.Failure<TrustResponse>(Error.Conflict(
                "Trust.LocalityAlreadyClaimed", "This Samaaj already has a Trust for that locality."));
        }

        var trust = Domain.Trusts.Trust.Create(tenantId, localityId, command.Name, clock.UtcNow);

        if (!await trusts.TryAddAsync(trust, cancellationToken))
        {
            // The read above raced another request. The unique index on
            // (TenantId, LocalityId) is the real guarantee - see
            // ITrustRepository.TryAddAsync.
            return Result.Failure<TrustResponse>(Error.Conflict(
                "Trust.LocalityAlreadyClaimed", "This Samaaj already has a Trust for that locality."));
        }

        return Result.Success(trust.ToResponse());
    }
}
