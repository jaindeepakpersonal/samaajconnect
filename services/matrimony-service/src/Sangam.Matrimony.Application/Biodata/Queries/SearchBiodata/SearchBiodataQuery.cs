using FluentValidation;
using MediatR;
using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Application.Common;
using Sangam.Matrimony.Application.Security;
using Sangam.Matrimony.Domain.Biodata;

namespace Sangam.Matrimony.Application.Biodata.Queries.SearchBiodata;

/// <summary>
/// Searches active biodata in the caller's own Samaaj, excluding the caller.
/// Capped at 50 results, matching `SearchMembersQuery`'s own default limit -
/// this platform's scale (a few thousand members per Samaaj) does not need
/// more.
/// </summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record SearchBiodataQuery(
    string? Gender,
    int? MinAge,
    int? MaxAge,
    Guid? MaritalStatusId,
    int? MinHeightCm,
    int? MaxHeightCm,
    string? Diet,
    Guid? LocalityId,
    bool ExcludeOwnGautra) : IQuery<IReadOnlyList<BiodataResponse>>;

public sealed class SearchBiodataQueryValidator : AbstractValidator<SearchBiodataQuery>
{
    public SearchBiodataQueryValidator()
    {
        RuleFor(x => x.Gender)
            .Must(value => value is null || Enum.TryParse<Gender>(value, ignoreCase: true, out _))
            .WithMessage($"Gender must be one of: {string.Join(", ", Enum.GetNames<Gender>())}.");

        RuleFor(x => x.Diet)
            .Must(value => value is null || Enum.TryParse<Domain.Biodata.Diet>(value, ignoreCase: true, out _))
            .WithMessage($"Diet must be one of: {string.Join(", ", Enum.GetNames<Domain.Biodata.Diet>())}.");

        RuleFor(x => x.MinAge).GreaterThanOrEqualTo(BiodataProfile.MinimumAge).When(x => x.MinAge.HasValue);
        RuleFor(x => x.MaxAge).GreaterThanOrEqualTo(BiodataProfile.MinimumAge).When(x => x.MaxAge.HasValue);
    }
}

public sealed class SearchBiodataQueryHandler(
    IBiodataRepository biodata, ICurrentUser currentUser, IDateTimeProvider clock)
    : IRequestHandler<SearchBiodataQuery, Result<IReadOnlyList<BiodataResponse>>>
{
    public async Task<Result<IReadOnlyList<BiodataResponse>>> Handle(
        SearchBiodataQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IReadOnlyList<BiodataResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        // "Exclude my own Gautra" needs the caller's own Gautra, read from
        // their own (possibly inactive, possibly absent) biodata rather than
        // requiring them to already be an active candidate themselves.
        Guid? excludeGautraId = null;

        if (query.ExcludeOwnGautra)
        {
            var own = await biodata.GetByIdAsync(memberId, cancellationToken);
            excludeGautraId = own?.GautraId;
        }

        var filters = new BiodataSearchFilters(
            memberId,
            query.Gender is null ? null : Enum.Parse<Gender>(query.Gender, ignoreCase: true),
            query.MinAge,
            query.MaxAge,
            query.MaritalStatusId,
            query.MinHeightCm,
            query.MaxHeightCm,
            query.Diet is null ? null : Enum.Parse<Domain.Biodata.Diet>(query.Diet, ignoreCase: true),
            query.LocalityId,
            excludeGautraId);

        var results = await biodata.SearchAsync(filters, cancellationToken);
        var now = clock.UtcNow;

        return Result.Success<IReadOnlyList<BiodataResponse>>(
            [.. results.Select(profile => profile.ToResponse(now))]);
    }
}
