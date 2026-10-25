using MediatR;
using Sangam.Matrimony.Api.Extensions;
using Sangam.Matrimony.Application.Biodata;
using Sangam.Matrimony.Application.Biodata.Commands.OptIntoMatrimony;
using Sangam.Matrimony.Application.Biodata.Commands.UpdateBiodata;
using Sangam.Matrimony.Application.Biodata.Commands.WithdrawMatrimonyConsent;
using Sangam.Matrimony.Application.Biodata.Queries.GetBiodata;
using Sangam.Matrimony.Application.Biodata.Queries.GetMatrimonyConsentNotice;
using Sangam.Matrimony.Application.Biodata.Queries.GetMyBiodata;
using Sangam.Matrimony.Application.Biodata.Queries.GetMyEligibility;
using Sangam.Matrimony.Application.Biodata.Queries.SearchBiodata;

namespace Sangam.Matrimony.Api.Endpoints;

/// <summary>
/// Thin mapping only (CLAUDE.md section 4.6): bind, build the request, send,
/// map the Result. Any `if` past input binding belongs in a handler.
/// </summary>
public static class BiodataEndpoints
{
    public static IEndpointRouteBuilder MapBiodataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/matrimony").WithTags("Matrimony biodata");

        group.MapGet("/consent-notice", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetMatrimonyConsentNoticeQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetMatrimonyConsentNotice")
            .WithSummary("What a member is shown before opting into matrimony search.")
            .Produces<MatrimonyConsentNoticeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/eligibility/me", async (ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new GetMyMatrimonyEligibilityQuery(), cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .WithName("GetMyMatrimonyEligibility")
            .WithSummary("Whether the caller may keep a biodata: Eligible, NotEligible, or Unknown until their marital status is set.")
            .Produces<MatrimonyEligibilityResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/biodata/me", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetMyBiodataQuery(), cancellationToken);

                // 204, not 200-with-a-null-body: "you have never opted in" is
                // unambiguous as an HTTP status, where a null JSON body is one
                // more shape every caller would have to specifically handle.
                return result.ToApiResult(value => value is null ? Results.NoContent() : Results.Ok(value));
            })
            .RequireAuthorization()
            .WithName("GetMyBiodata")
            .WithSummary("The caller's own biodata, or 204 if they have never opted in.")
            .Produces<MyBiodataResponse>()
            .ProducesProblem(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/biodata/me/opt-in", async (
                OptIntoMatrimonyRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var command = new OptIntoMatrimonyCommand(
                    request.FullName, request.Gender, request.DateOfBirth, request.GautraId,
                    request.ProfessionId, request.QualificationId, request.CityId, request.LocalityId,
                    request.HeightCm, request.MaritalStatusId, request.Diet, request.FamilyType,
                    request.FatherOccupation, request.MotherOccupation, request.SiblingsCount,
                    request.Manglik, request.HoroscopeNotes, request.AboutMe, request.PreferredAgeMin,
                    request.PreferredAgeMax, request.PartnerPreferenceNotes,
                    request.ConsentGiven, request.NoticeVersion);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("OptIntoMatrimony")
            .WithSummary("Opt into matrimony search, or rejoin after a withdrawal.")
            .Produces<MyBiodataResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPatch("/biodata/me", async (
                UpdateBiodataRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var command = new UpdateBiodataCommand(
                    request.FullName, request.Gender, request.DateOfBirth, request.GautraId,
                    request.ProfessionId, request.QualificationId, request.CityId, request.LocalityId,
                    request.HeightCm, request.MaritalStatusId, request.Diet, request.FamilyType,
                    request.FatherOccupation, request.MotherOccupation, request.SiblingsCount,
                    request.Manglik, request.HoroscopeNotes, request.AboutMe, request.PreferredAgeMin,
                    request.PreferredAgeMax, request.PartnerPreferenceNotes);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("UpdateBiodata")
            .WithSummary("Edit an already-opted-in biodata profile.")
            .Produces<MyBiodataResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/biodata/me/consent", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new WithdrawMatrimonyConsentCommand(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("WithdrawMatrimonyConsent")
            .WithSummary("Take the caller's own biodata out of search. Proposal history is kept.")
            .Produces<WithdrawMatrimonyConsentResult>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/biodata/search", async (
                string? gender, int? minAge, int? maxAge, Guid? maritalStatusId,
                int? minHeightCm, int? maxHeightCm, string? diet, Guid? localityId,
                bool? excludeOwnGautra, ISender sender, CancellationToken cancellationToken) =>
            {
                var query = new SearchBiodataQuery(
                    gender, minAge, maxAge, maritalStatusId, minHeightCm, maxHeightCm,
                    diet, localityId, excludeOwnGautra ?? false);

                var result = await sender.Send(query, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("SearchBiodata")
            .WithSummary("Search active biodata in the caller's own Samaaj.")
            .Produces<IReadOnlyList<BiodataResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/biodata/{memberId:guid}", async (
                Guid memberId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetBiodataQuery(memberId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetBiodata")
            .WithSummary("One candidate's full biodata.")
            .Produces<BiodataResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record OptIntoMatrimonyRequest(
        string FullName,
        string Gender,
        // A raw string, not DateOnly - see OptIntoMatrimonyCommand's own
        // note on why a DateOnly-typed wire field crashes ASP.NET Core's
        // JSON model binding with an unhandled 500 on an empty string.
        string? DateOfBirth,
        // Raw strings, not Guid? - see OptIntoMatrimonyCommand's own note on
        // why a Guid?-typed wire field crashes ASP.NET Core's JSON model
        // binding on the literal text "null" a mis-bound `<select>` can send.
        string? GautraId,
        string? ProfessionId,
        string? QualificationId,
        string? CityId,
        string? LocalityId,
        int? HeightCm,
        string? MaritalStatusId,
        string? Diet,
        string? FamilyType,
        string? FatherOccupation,
        string? MotherOccupation,
        int? SiblingsCount,
        bool? Manglik,
        string? HoroscopeNotes,
        string? AboutMe,
        int? PreferredAgeMin,
        int? PreferredAgeMax,
        string? PartnerPreferenceNotes,
        bool ConsentGiven,
        string NoticeVersion);

    public sealed record UpdateBiodataRequest(
        string FullName,
        string Gender,
        string? DateOfBirth,
        string? GautraId,
        string? ProfessionId,
        string? QualificationId,
        string? CityId,
        string? LocalityId,
        int? HeightCm,
        string? MaritalStatusId,
        string? Diet,
        string? FamilyType,
        string? FatherOccupation,
        string? MotherOccupation,
        int? SiblingsCount,
        bool? Manglik,
        string? HoroscopeNotes,
        string? AboutMe,
        int? PreferredAgeMin,
        int? PreferredAgeMax,
        string? PartnerPreferenceNotes);
}
