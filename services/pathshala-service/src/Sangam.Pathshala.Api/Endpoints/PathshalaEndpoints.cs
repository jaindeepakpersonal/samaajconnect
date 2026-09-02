using MediatR;
using Sangam.Pathshala.Api.Extensions;
using Sangam.Pathshala.Application.Enrolments.Commands;
using Sangam.Pathshala.Application.Enrolments.Queries;
using Sangam.Pathshala.Application.Pathshalas;
using Sangam.Pathshala.Application.Pathshalas.Commands;
using Sangam.Pathshala.Application.Pathshalas.Queries;

namespace Sangam.Pathshala.Api.Endpoints;

/// <summary>
/// Thin mapping only (CLAUDE.md section 4.6): bind, build the request, send,
/// map the Result. Any `if` past input binding belongs in a handler.
/// </summary>
public static class PathshalaEndpoints
{
    public static IEndpointRouteBuilder MapPathshalaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/pathshala").WithTags("Jain Pathshala");

        MapPathshalas(group);
        MapClasses(group);
        MapEnrolments(group);
        MapStudentViews(group);

        return app;
    }

    // ---- The Pathshala itself -------------------------------------------

    private static void MapPathshalas(RouteGroupBuilder group)
    {
        group.MapGet("/pathshalas", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListPathshalasQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListPathshalas")
            .WithSummary("This Samaaj's Pathshalas, with class and teacher counts.")
            .Produces<IReadOnlyList<PathshalaResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/pathshalas", async (
                CreatePathshalaRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var command = new CreatePathshalaCommand(
                    request.Name, request.Address, request.ContactPerson);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/pathshala/pathshalas/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("CreatePathshala")
            .WithSummary("Create the master record. Super Admin only (DATA-MODEL.md section 9).")
            .Produces<PathshalaResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/pathshalas/{id:guid}", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetPathshalaQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetPathshala")
            .WithSummary("One Pathshala with its sessions and classes.")
            .Produces<PathshalaDetailResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/pathshalas/{id:guid}/sessions", async (
                Guid id, OpenSessionRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new OpenSessionCommand(id, request.Label, request.StartDate, request.EndDate),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("OpenSession")
            .WithSummary("Open an academic session. It becomes the current one.")
            .Produces<PathshalaDetailResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/pathshalas/{id:guid}", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new DeactivatePathshalaCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("DeactivatePathshala")
            .WithSummary("Stop a Pathshala operating. Records are kept; enrolments stop.")
            .Produces<PathshalaResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    // ---- Classes ---------------------------------------------------------

    private static void MapClasses(RouteGroupBuilder group)
    {
        group.MapPost("/pathshalas/{id:guid}/classes", async (
                Guid id, CreateClassRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new CreateClassCommand(id, request.SessionId, request.Name, request.RoomLabel),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("CreateClass")
            .WithSummary("Add a class to a session.")
            .Produces<ClassResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/classes/{classId:guid}/schedule", async (
                Guid classId, AddSlotRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new AddClassSlotCommand(
                        classId, request.DayOfWeek, request.StartTime, request.EndTime),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("AddClassSlot")
            .WithSummary("Add a weekly slot. Overlapping slots are refused.")
            .Produces<ClassResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/classes/{classId:guid}/teachers", async (
                Guid classId, TeacherRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new AssignTeacherCommand(classId, request.TeacherMemberId, request.Assign),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("AssignTeacher")
            .WithSummary("Assign or remove a teacher on this class.")
            .Produces<ClassResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/classes/{classId:guid}/roll", async (
                Guid classId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetClassRollQuery(classId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetClassRoll")
            .WithSummary("Who is on this class's roll. Teachers of this class, and administrators.")
            .Produces<IReadOnlyList<EnrolmentResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/classes/{classId:guid}/register", async (
                Guid classId,
                DateOnly date,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new GetClassRegisterQuery(classId, date), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetClassRegister")
            .WithSummary("The register already marked for one date, so amending it is not a guess.")
            .Produces<IReadOnlyList<RegisterEntryResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/classes/{classId:guid}/exams", async (
                Guid classId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListClassExamsQuery(classId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListClassExams")
            .WithSummary("This class's exams and the marks recorded in each.")
            .Produces<IReadOnlyList<ClassExamResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/classes/{classId:guid}/attendance", async (
                Guid classId, MarkAttendanceRequest request, ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new MarkAttendanceCommand(classId, request.ClassDate, request.Marks),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("MarkAttendance")
            .WithSummary(
                "Mark the whole register for one date. Re-marking amends; the unique index on "
                + "(enrolment, date), not this endpoint, is what prevents a duplicate.")
            .Produces<MarkAttendanceResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/classes/{classId:guid}/exams", async (
                Guid classId, ScheduleExamRequest request, ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new ScheduleExamCommand(
                        classId, request.Title, request.ExamDate, request.MaxScore),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ScheduleExam")
            .WithSummary("Set an exam for this class.")
            .Produces<ExamResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/exams/{examId:guid}/results", async (
                Guid examId, RecordResultRequest request, ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new RecordExamResultCommand(
                        examId, request.EnrolmentId, request.Score, request.Grade),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("RecordExamResult")
            .WithSummary("Record one student's mark, or correct one already recorded.")
            .Produces<ExamResultResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    // ---- Enrolment -------------------------------------------------------

    private static void MapEnrolments(RouteGroupBuilder group)
    {
        group.MapPost("/pathshalas/{id:guid}/enrollments", async (
                Guid id, EnrolRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new RequestEnrolmentCommand(id, request.ChildProfileId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("RequestEnrolment")
            .WithSummary(
                "Ask for a place for a child. Somebody at the Pathshala places them in a class.")
            .Produces<EnrolmentResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/pathshalas/{id:guid}/enrollments/requests", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new ListEnrolmentRequestsQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListEnrolmentRequests")
            .WithSummary("Requests waiting to be placed.")
            .Produces<IReadOnlyList<EnrolmentResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/enrollments/{enrolmentId:guid}/placement", async (
                Guid enrolmentId, PlacementRequest request, ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new PlaceStudentCommand(enrolmentId, request.ClassId, request.Place),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("PlaceStudent")
            .WithSummary("Place a requested child in a class, or turn the request down.")
            .Produces<EnrolmentResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/enrollments/{enrolmentId:guid}", async (
                Guid enrolmentId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new WithdrawStudentCommand(enrolmentId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("WithdrawStudent")
            .WithSummary("Take a student off the roll. Their attendance and results are kept.")
            .Produces<EnrolmentResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/enrollments", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListMyEnrolmentsQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListMyEnrolments")
            .WithSummary("Every place this member asked for, or holds themselves.")
            .Produces<IReadOnlyList<EnrolmentResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    // ---- The student's own views -----------------------------------------

    private static void MapStudentViews(RouteGroupBuilder group)
    {
        group.MapGet("/enrollments/{enrolmentId:guid}/my-class", async (
                Guid enrolmentId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetMyClassQuery(enrolmentId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetMyClass")
            .WithSummary("The class, room, session, timetable and teachers.")
            .Produces<MyClassResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/enrollments/{enrolmentId:guid}/attendance", async (
                Guid enrolmentId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new GetMyAttendanceQuery(enrolmentId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetMyAttendance")
            .WithSummary("Percentage, present, absent and excused, with the days behind them.")
            .Produces<MyAttendanceResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/enrollments/{enrolmentId:guid}/exams", async (
                Guid enrolmentId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetMyExamsQuery(enrolmentId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetMyExams")
            .WithSummary("Upcoming, awaiting result, and completed.")
            .Produces<IReadOnlyList<MyExamResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/enrollments/{enrolmentId:guid}/progress", async (
                Guid enrolmentId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new GetMyProgressQuery(enrolmentId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetMyProgress")
            .WithSummary("Attendance and average score, computed rather than stored.")
            .Produces<MyProgressResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    // ---- Wire shapes ------------------------------------------------------

    public sealed record CreatePathshalaRequest(string Name, string? Address, string? ContactPerson);

    public sealed record OpenSessionRequest(string Label, DateOnly StartDate, DateOnly EndDate);

    public sealed record CreateClassRequest(Guid SessionId, string Name, string? RoomLabel);

    public sealed record AddSlotRequest(string DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);

    /// <summary>
    /// <paramref name="Assign"/> is required and has no default: an endpoint
    /// whose safest value is implicit is one where a mistyped request quietly
    /// removes a teacher from a class.
    /// </summary>
    public sealed record TeacherRequest(Guid TeacherMemberId, bool Assign);

    public sealed record MarkAttendanceRequest(
        DateOnly ClassDate, IReadOnlyList<AttendanceMark> Marks);

    public sealed record ScheduleExamRequest(string Title, DateOnly ExamDate, int MaxScore);

    public sealed record RecordResultRequest(Guid EnrolmentId, int Score, string? Grade);

    public sealed record EnrolRequest(Guid ChildProfileId);

    /// <summary>Same reasoning as <see cref="TeacherRequest"/>: no default.</summary>
    public sealed record PlacementRequest(Guid? ClassId, bool Place);
}
