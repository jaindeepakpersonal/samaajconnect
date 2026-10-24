using MediatR;
using Sangam.Pathshala.Api.Extensions;
using Sangam.Pathshala.Application.Announcements;
using Sangam.Pathshala.Application.Donations;
using Sangam.Pathshala.Application.Pathshalas;
using Sangam.Pathshala.Application.Pathshalas.Commands;
using Sangam.Pathshala.Application.Pathshalas.Queries;

namespace Sangam.Pathshala.Api.Endpoints;

/// <summary>
/// The admin panel's Pathshala tabs - teachers, students, progress, attendance,
/// exams, donations, announcements - and a parent's own announcements.
/// </summary>
/// <remarks>
/// Thin mapping only (CLAUDE.md section 4.6). Who may read or write each is the
/// handlers' data check; every refusal is a 404, as everywhere in this service.
/// </remarks>
public static class StaffEndpoints
{
    public static IEndpointRouteBuilder MapStaffEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/pathshala").WithTags("Jain Pathshala - staff");

        MapTeachers(group);
        MapReports(group);
        MapDonations(group);
        MapAnnouncements(group);

        return app;
    }

    private static void MapTeachers(RouteGroupBuilder group)
    {
        group.MapGet("/pathshalas/{id:guid}/teachers", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new ListTeachersQuery(id), cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .WithName("ListTeachers")
            .WithSummary("The Pathshala's teachers with their contact details. Its administrators and teachers only.")
            .Produces<IReadOnlyList<TeacherResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/pathshalas/{id:guid}/teachers", async (
                Guid id, TeacherRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new AddTeacherCommand(
                        id, request.FullName, request.Phone, request.Email, request.Qualification, request.UserId),
                    cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/pathshala/pathshalas/{id}/teachers/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("AddTeacher")
            .WithSummary("Add a teacher - who need not be a member of the Samaaj. userId links their login, if any.")
            .Produces<TeacherResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/pathshalas/{id:guid}/teachers/{teacherId:guid}", async (
                Guid id, Guid teacherId, TeacherRequest request, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(
                    new UpdateTeacherCommand(
                        id, teacherId, request.FullName, request.Phone, request.Email, request.Qualification,
                        request.UserId),
                    cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .WithName("UpdateTeacher")
            .WithSummary("Correct a teacher's details or login.")
            .Produces<TeacherResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/pathshalas/{id:guid}/teachers/{teacherId:guid}", async (
                Guid id, Guid teacherId, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new RemoveTeacherCommand(id, teacherId), cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .WithName("RemoveTeacher")
            .WithSummary("Remove a teacher and their class assignments. The registers they marked are kept.")
            .Produces<Guid>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapReports(RouteGroupBuilder group)
    {
        group.MapGet("/pathshalas/{id:guid}/students", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new ListStudentsQuery(id), cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .WithName("ListStudents")
            .WithSummary("Children with a place, and requests waiting for one, with the contact the parent gave.")
            .Produces<IReadOnlyList<StudentResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/pathshalas/{id:guid}/progress", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new GetProgressReportQuery(id), cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .WithName("GetProgressReport")
            .WithSummary("Every placed child's attendance and exam average.")
            .Produces<IReadOnlyList<ProgressRowResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/pathshalas/{id:guid}/attendance", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new GetAttendanceOverviewQuery(id), cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .WithName("GetAttendanceOverview")
            .WithSummary("Per class: days marked and how the register stands.")
            .Produces<IReadOnlyList<ClassAttendanceResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/pathshalas/{id:guid}/exams", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new ListPathshalaExamsQuery(id), cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .WithName("ListPathshalaExams")
            .WithSummary("Every exam across the classes the caller may see, with how far marking has got.")
            .Produces<IReadOnlyList<ExamOverviewResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapDonations(RouteGroupBuilder group)
    {
        group.MapGet("/pathshalas/{id:guid}/donations", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new ListDonationsQuery(id), cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .WithName("ListDonations")
            .WithSummary("The Pathshala's donation ledger, what has been received and what is announced and not yet paid, deleted entries left out of both.")
            .Produces<DonationLedgerResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/pathshalas/{id:guid}/donations", async (
                Guid id, DonationRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new RecordDonationCommand(
                        id, request.DonorName, request.DonorMemberId, request.Amount, request.DonatedOn,
                        request.Mode, request.Purpose, request.Paid ?? true),
                    cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/pathshala/pathshalas/{id}/donations/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("RecordDonation")
            .WithSummary("Record a donation - paid, with its receipt number, or announced (paid: false) and paid later. Amounts are integer paise.")
            .Produces<DonationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/donations/{donationId:guid}", async (
                Guid donationId, DonationCorrection request, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(
                    new UpdateDonationCommand(
                        donationId, request.DonorName, request.Amount, request.DonatedOn, request.Mode,
                        request.PaidOn, request.Purpose),
                    cancellationToken))
                .ToApiResult())
            .RequireAuthorization()
            .WithName("UpdateDonation")
            .WithSummary("Correct an entry. A paid one needs its mode and paid date; a pledge stays a pledge.")
            .Produces<DonationResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/donations/{donationId:guid}/paid", async (
                Guid donationId, PaidRequest request, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new MarkDonationPaidCommand(donationId, request.Mode, request.PaidOn), cancellationToken))
                .ToApiResult())
            .RequireAuthorization()
            .WithName("MarkDonationPaid")
            .WithSummary("An announced donation was paid: issues its receipt number. 409 once paid.")
            .Produces<DonationResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/donations/{donationId:guid}", async (
                Guid donationId, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new DeleteDonationCommand(donationId), cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .WithName("DeleteDonation")
            .WithSummary("Erase an announcement not yet paid. 409 once it has a receipt - void that instead.")
            .Produces<Guid>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/donations/{donationId:guid}/void", async (
                Guid donationId, VoidRequest request, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new VoidDonationCommand(donationId, request.Reason), cancellationToken))
                .ToApiResult())
            .RequireAuthorization()
            .WithName("VoidDonation")
            .WithSummary("Delete a paid entry from the ledger and its totals, with a reason. The row is kept, so its receipt number is never reissued. 409 on an announcement - erase that instead.")
            .Produces<DonationResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapAnnouncements(RouteGroupBuilder group)
    {
        group.MapGet("/pathshalas/{id:guid}/announcements", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new ListAnnouncementsQuery(id), cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .WithName("ListAnnouncements")
            .WithSummary("What this Pathshala has sent its parents, newest first.")
            .Produces<IReadOnlyList<AnnouncementResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/pathshalas/{id:guid}/announcements", async (
                Guid id, AnnouncementRequest request, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new SendAnnouncementCommand(id, request.Title, request.Body), cancellationToken))
                .ToApiResult())
            .RequireAuthorization()
            .WithName("SendAnnouncement")
            .WithSummary("Message the parents of every child with a place or a request. A teacher or administrator of it.")
            .Produces<AnnouncementResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/announcements", async (ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new ListMyAnnouncementsQuery(), cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .WithName("ListMyAnnouncements")
            .WithSummary("Announcements from every Pathshala where the caller has a child with a place or a request.")
            .Produces<IReadOnlyList<AnnouncementResponse>>();
    }

    public sealed record TeacherRequest(
        string FullName, string? Phone, string? Email, string? Qualification, string? UserId);

    /// <summary><c>Paid</c> defaults to true: a donation recorded with no word otherwise has been received.</summary>
    public sealed record DonationRequest(
        string DonorName, string? DonorMemberId, long Amount, DateOnly DonatedOn, string? Mode, string? Purpose, bool? Paid);

    public sealed record DonationCorrection(
        string DonorName, long Amount, DateOnly DonatedOn, string? Mode, DateOnly? PaidOn, string? Purpose);

    public sealed record PaidRequest(string Mode, DateOnly PaidOn);

    public sealed record VoidRequest(string Reason);

    public sealed record AnnouncementRequest(string Title, string Body);
}
