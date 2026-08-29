using FluentAssertions;
using Sangam.Pathshala.Domain.Enrolments;
using Sangam.Pathshala.Domain.Pathshalas;
using Sangam.Pathshala.Domain.Pathshalas.Events;
using Xunit;

namespace Sangam.Pathshala.UnitTests;

/// <summary>
/// How a Pathshala is organised: sessions, classes, timetables and teachers.
/// </summary>
public sealed class PathshalaTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static Domain.Pathshalas.Pathshala APathshala() =>
        Domain.Pathshalas.Pathshala.Create(
            TenantId, "Shri Mahavir Jain Pathshala", "Hiran Magri", "Smt. Kavita Jain", Now);

    private static (Domain.Pathshalas.Pathshala Pathshala, PathshalaClass Class) AClass()
    {
        var pathshala = APathshala();

        var session = pathshala.OpenSession(
            "2026-27", new DateOnly(2026, 3, 1), new DateOnly(2027, 2, 28), Now);

        return (pathshala, pathshala.AddClass(session.Id, "Class 8", "Room 2")!);
    }

    // ---- One current session -----------------------------------------------

    [Fact]
    public void A_new_Pathshala_takes_no_enrolments_until_a_session_is_open()
    {
        // "Current session" is what decides where a new enrolment lands, so a
        // Pathshala with none has nowhere to put a child.
        APathshala().AcceptsEnrolments.Should().BeFalse();
    }

    [Fact]
    public void Opening_a_session_closes_the_previous_one()
    {
        var pathshala = APathshala();

        var first = pathshala.OpenSession(
            "2025-26", new DateOnly(2025, 3, 1), new DateOnly(2026, 2, 28), Now);

        var second = pathshala.OpenSession(
            "2026-27", new DateOnly(2026, 3, 1), new DateOnly(2027, 2, 28), Now);

        first.IsCurrent.Should().BeFalse(
            "two current sessions means a new enrolment has two places it might land, "
            + "and a child enrolled into last year appears on no register");

        second.IsCurrent.Should().BeTrue();
        pathshala.CurrentSession!.Id.Should().Be(second.Id);
    }

    [Fact]
    public void The_old_session_keeps_its_classes()
    {
        // A session ending is not a reason to lose what happened during it.
        var (pathshala, oldClass) = AClass();

        pathshala.OpenSession(
            "2027-28", new DateOnly(2027, 3, 1), new DateOnly(2028, 2, 29), Now);

        pathshala.FindClass(oldClass.Id).Should().NotBeNull();
    }

    [Fact]
    public void Opening_a_session_announces_it()
    {
        var pathshala = APathshala();

        pathshala.OpenSession("2026-27", new DateOnly(2026, 3, 1), new DateOnly(2027, 2, 28), Now);

        pathshala.DomainEvents.Should()
            .ContainItemsAssignableTo<AcademicSessionOpenedDomainEvent>();
    }

    [Fact]
    public void A_class_cannot_be_added_to_a_session_that_does_not_exist()
    {
        APathshala().AddClass(Guid.NewGuid(), "Class 8", null).Should().BeNull();
    }

    [Fact]
    public void A_deactivated_Pathshala_takes_no_enrolments()
    {
        var (pathshala, _) = AClass();

        pathshala.AcceptsEnrolments.Should().BeTrue();

        pathshala.Deactivate(Now);

        pathshala.AcceptsEnrolments.Should().BeFalse();
        pathshala.Status.Should().Be(PathshalaStatus.Inactive);
    }

    // ---- Teachers ----------------------------------------------------------

    [Fact]
    public void Assigning_the_same_teacher_twice_adds_one_assignment()
    {
        var (_, pathshalaClass) = AClass();
        var teacherId = Guid.NewGuid();

        pathshalaClass.AssignTeacher(teacherId, Now).Should().BeTrue();
        pathshalaClass.AssignTeacher(teacherId, Now).Should().BeFalse();

        pathshalaClass.Teachers.Should().ContainSingle();
    }

    [Fact]
    public void A_teacher_teaches_only_the_classes_they_are_assigned_to()
    {
        // The check that stops Pathshala.Attendance.Write meaning "any class".
        var (pathshala, first) = AClass();
        var second = pathshala.AddClass(pathshala.CurrentSession!.Id, "Class 9", null)!;
        var teacherId = Guid.NewGuid();

        first.AssignTeacher(teacherId, Now);

        first.IsTaughtBy(teacherId).Should().BeTrue();
        second.IsTaughtBy(teacherId).Should().BeFalse();
        pathshala.IsTeacher(teacherId).Should().BeTrue("they teach somewhere here");
    }

    [Fact]
    public void Removing_a_teacher_who_does_not_teach_the_class_changes_nothing()
    {
        var (_, pathshalaClass) = AClass();

        pathshalaClass.RemoveTeacher(Guid.NewGuid()).Should().BeFalse();
    }

    // ---- The timetable -----------------------------------------------------

    [Fact]
    public void A_slot_that_ends_before_it_starts_is_refused()
    {
        var (_, pathshalaClass) = AClass();

        pathshalaClass.AddSlot(DayOfWeek.Sunday, new TimeOnly(11, 0), new TimeOnly(10, 0))
            .Should().BeFalse();
    }

    [Fact]
    public void Overlapping_slots_on_one_day_are_refused()
    {
        // Two overlapping slots describe a class that meets twice at once,
        // which nothing downstream can render.
        var (_, pathshalaClass) = AClass();

        pathshalaClass.AddSlot(DayOfWeek.Sunday, new TimeOnly(10, 0), new TimeOnly(12, 0))
            .Should().BeTrue();

        pathshalaClass.AddSlot(DayOfWeek.Sunday, new TimeOnly(11, 0), new TimeOnly(13, 0))
            .Should().BeFalse();

        // Touching, not overlapping.
        pathshalaClass.AddSlot(DayOfWeek.Sunday, new TimeOnly(12, 0), new TimeOnly(13, 0))
            .Should().BeTrue();

        // A different day is never a clash.
        pathshalaClass.AddSlot(DayOfWeek.Wednesday, new TimeOnly(11, 0), new TimeOnly(13, 0))
            .Should().BeTrue();
    }

    [Fact]
    public void A_class_with_no_timetable_meets_on_any_day()
    {
        // An empty schedule means "unknown", not "never". A Pathshala that has
        // not filled its timetable in should still be able to take a register.
        var (_, pathshalaClass) = AClass();

        pathshalaClass.MeetsOn(new DateOnly(2026, 3, 4)).Should().BeTrue();
    }

    [Fact]
    public void A_class_with_a_timetable_meets_only_on_its_days()
    {
        var (_, pathshalaClass) = AClass();

        pathshalaClass.AddSlot(DayOfWeek.Sunday, new TimeOnly(10, 0), new TimeOnly(12, 0));

        pathshalaClass.MeetsOn(new DateOnly(2026, 3, 1)).Should().BeTrue("that is a Sunday");
        pathshalaClass.MeetsOn(new DateOnly(2026, 3, 4)).Should().BeFalse("that is a Wednesday");
    }
}

/// <summary>
/// A place at a Pathshala: asked for, granted, and given up.
/// </summary>
public sealed class StudentEnrolmentTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PathshalaId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static StudentEnrolment ARequest() => StudentEnrolment.Request(
        TenantId, PathshalaId, Guid.NewGuid(), ParentId, Now);

    private static StudentEnrolment APlacement()
    {
        var enrolment = ARequest();

        enrolment.PlaceIn(Guid.NewGuid(), Guid.NewGuid(), Now);

        return enrolment;
    }

    [Fact]
    public void A_request_is_not_on_the_roll()
    {
        // Nothing can be recorded against a child nobody has placed, which is
        // why an unplaced request grants access to nothing.
        ARequest().IsOnRoll.Should().BeFalse();
    }

    [Fact]
    public void Placing_puts_them_on_the_roll_and_announces_it()
    {
        var enrolment = APlacement();

        enrolment.IsOnRoll.Should().BeTrue();
        enrolment.Status.Should().Be(EnrolmentStatus.Active);
        enrolment.EnrolledAt.Should().Be(Now);

        enrolment.DomainEvents.Should()
            .ContainItemsAssignableTo<Domain.Enrolments.Events.StudentEnrolledDomainEvent>();
    }

    [Fact]
    public void Only_a_waiting_request_can_be_placed_or_declined()
    {
        var enrolment = APlacement();

        enrolment.PlaceIn(Guid.NewGuid(), Guid.NewGuid(), Now).Should().BeFalse();
        enrolment.Decline(Now).Should().BeFalse();
    }

    [Fact]
    public void Withdrawing_keeps_the_record()
    {
        // A child who leaves in March still attended from June, and a Pathshala
        // asked what its attendance was that year has to be able to answer.
        var enrolment = APlacement();
        var classId = enrolment.ClassId;

        enrolment.Withdraw(Now).Should().BeTrue();

        enrolment.Status.Should().Be(EnrolmentStatus.Withdrawn);
        enrolment.ClassId.Should().Be(classId, "their history is still about that class");
        enrolment.IsOnRoll.Should().BeFalse();
    }

    [Fact]
    public void An_unplaced_request_cannot_be_withdrawn()
    {
        ARequest().Withdraw(Now).Should().BeFalse();
    }

    // ---- Who it belongs to --------------------------------------------------

    [Fact]
    public void The_parent_who_asked_owns_it()
    {
        var enrolment = ARequest();

        enrolment.BelongsTo(ParentId).Should().BeTrue();
        enrolment.BelongsTo(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void And_so_does_the_child_once_they_have_an_account()
    {
        // The one promise the conversion flow makes about this service.
        var enrolment = ARequest();
        var studentUserId = Guid.NewGuid();

        enrolment.BelongsTo(studentUserId).Should().BeFalse();

        enrolment.LinkTo(studentUserId).Should().BeTrue();

        enrolment.BelongsTo(studentUserId).Should().BeTrue();
        enrolment.BelongsTo(ParentId).Should().BeTrue("the parent does not lose access");
    }

    [Fact]
    public void Linking_the_same_account_twice_changes_nothing()
    {
        // Delivery is at least once, so the second copy of the conversion event
        // is the ordinary case rather than an error.
        var enrolment = ARequest();
        var studentUserId = Guid.NewGuid();

        enrolment.LinkTo(studentUserId).Should().BeTrue();
        enrolment.LinkTo(studentUserId).Should().BeFalse();
    }
}

/// <summary>Exams, and the marks recorded against them.</summary>
public sealed class ExamTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClassId = Guid.NewGuid();

    private static Exam AnExam(int maxScore = 50) =>
        Exam.Schedule(TenantId, ClassId, "Jain History", new DateOnly(2026, 8, 10), maxScore);

    [Fact]
    public void A_score_outside_the_paper_is_not_a_mark()
    {
        var exam = AnExam();

        exam.Accepts(0).Should().BeTrue();
        exam.Accepts(50).Should().BeTrue();
        exam.Accepts(51).Should().BeFalse();
        exam.Accepts(-1).Should().BeFalse();
    }

    [Fact]
    public void An_exam_is_sat_on_its_date_not_after_it()
    {
        var exam = AnExam();

        exam.HasBeenSat(new DateOnly(2026, 8, 9)).Should().BeFalse();
        exam.HasBeenSat(new DateOnly(2026, 8, 10)).Should().BeTrue();
    }

    [Fact]
    public void An_exam_marked_out_of_nothing_is_refused()
    {
        var schedule = () => Exam.Schedule(
            TenantId, ClassId, "Nothing", new DateOnly(2026, 8, 10), maxScore: 0);

        schedule.Should().Throw<ArgumentOutOfRangeException>();
    }
}
