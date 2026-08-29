using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Pathshala.Domain.Enrolments;

namespace Sangam.Pathshala.Infrastructure.Persistence.Configurations;

public sealed class StudentEnrolmentConfiguration : IEntityTypeConfiguration<StudentEnrolment>
{
    public void Configure(EntityTypeBuilder<StudentEnrolment> builder)
    {
        builder.ToTable("student_enrolments");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        // One place per child per Pathshala, whatever its state. A parent who
        // submits the form twice must not produce two rows: the child would
        // appear on two rolls, be marked twice, and have two attendance
        // percentages neither of which is right.
        builder.HasIndex(e => new { e.PathshalaId, e.ChildProfileId }).IsUnique();

        // The queue, the roll, and "what did I ask for?" - the three ways this
        // table is read.
        builder.HasIndex(e => new { e.PathshalaId, e.Status });
        builder.HasIndex(e => new { e.ClassId, e.Status });
        builder.HasIndex(e => e.RequestedByMemberId);
        builder.HasIndex(e => e.StudentUserId);
        builder.HasIndex(e => e.ChildProfileId);

        builder.Ignore(e => e.DomainEvents);
    }
}

public sealed class AttendanceEntryConfiguration : IEntityTypeConfiguration<AttendanceEntry>
{
    public void Configure(EntityTypeBuilder<AttendanceEntry> builder)
    {
        builder.ToTable("attendance");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(10);

        // ---------------------------------------------------------------
        // This index is what keeps a child's attendance record true.
        //
        // A teacher submits a register of twenty-five from a phone, and often
        // submits it twice because the first attempt looked like it failed. The
        // check in MarkAttendanceCommandHandler does not stop the second one:
        // both submissions read no existing row before either writes.
        //
        // Every number this service reports - the percentage, the present
        // count, the progress screen - is a count over this table. A duplicate
        // does not fail loudly; it quietly inflates one child's record, and
        // there is nothing on any screen to notice it by.
        // ---------------------------------------------------------------
        builder.HasIndex(a => new { a.EnrolmentId, a.ClassDate }).IsUnique();

        // Reading back a whole class's register for one date, which is what
        // re-marking needs before it can amend.
        builder.HasIndex(a => new { a.ClassId, a.ClassDate });
    }
}

public sealed class ExamConfiguration : IEntityTypeConfiguration<Exam>
{
    public void Configure(EntityTypeBuilder<Exam> builder)
    {
        builder.ToTable("exams");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Title).IsRequired().HasMaxLength(200);

        builder.HasIndex(e => new { e.ClassId, e.ExamDate });

        builder.Ignore(e => e.DomainEvents);
    }
}

public sealed class ExamResultConfiguration : IEntityTypeConfiguration<ExamResult>
{
    public void Configure(EntityTypeBuilder<ExamResult> builder)
    {
        builder.ToTable("exam_results");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Grade).HasMaxLength(10);

        // One mark per student per exam, for the same reason as attendance:
        // the average score the progress view reports would otherwise depend on
        // which of two rows happened to be read.
        builder.HasIndex(r => new { r.ExamId, r.EnrolmentId }).IsUnique();

        builder.HasIndex(r => r.EnrolmentId);
    }
}
