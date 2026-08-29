using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Pathshala.Domain.Enrolments;
using Sangam.Pathshala.Domain.Pathshalas;

namespace Sangam.Pathshala.Infrastructure.Persistence.Configurations;

public sealed class PathshalaConfiguration : IEntityTypeConfiguration<Domain.Pathshalas.Pathshala>
{
    public void Configure(EntityTypeBuilder<Domain.Pathshalas.Pathshala> builder)
    {
        builder.ToTable("pathshalas");
        builder.HasKey(p => p.Id);

        // Domain-assigned, like every key in this service. Left as EF's default
        // an entity added to an already-tracked graph comes back Modified rather
        // than Added and the save fails against a row that was never there -
        // the trap member-family-service and identity-tenant-service both
        // record.
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Address).HasMaxLength(500);
        builder.Property(p => p.ContactPerson).HasMaxLength(200);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(p => new { p.TenantId, p.Status });

        builder.HasMany(p => p.Sessions)
            .WithOne()
            .HasForeignKey(s => s.PathshalaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Classes)
            .WithOne()
            .HasForeignKey(c => c.PathshalaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Domain.Pathshalas.Pathshala.Sessions))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Metadata.FindNavigation(nameof(Domain.Pathshalas.Pathshala.Classes))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(p => p.DomainEvents);
    }
}

public sealed class AcademicSessionConfiguration : IEntityTypeConfiguration<AcademicSession>
{
    public void Configure(EntityTypeBuilder<AcademicSession> builder)
    {
        builder.ToTable("academic_sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Label).IsRequired().HasMaxLength(50);

        // One session per label per Pathshala. Two called "2026-27" makes every
        // record naming one ambiguous, with no way to tell them apart after.
        builder.HasIndex(s => new { s.PathshalaId, s.Label }).IsUnique();
    }
}

public sealed class PathshalaClassConfiguration : IEntityTypeConfiguration<PathshalaClass>
{
    public void Configure(EntityTypeBuilder<PathshalaClass> builder)
    {
        // `classes`, not `pathshala_classes`. The CLR type carries a prefix
        // only because `class` is a C# keyword.
        builder.ToTable("classes");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        builder.Property(c => c.RoomLabel).HasMaxLength(50);

        builder.HasIndex(c => c.SessionId);

        builder.HasMany(c => c.Schedule)
            .WithOne()
            .HasForeignKey(s => s.ClassId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Teachers)
            .WithOne()
            .HasForeignKey(t => t.ClassId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(PathshalaClass.Schedule))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Metadata.FindNavigation(nameof(PathshalaClass.Teachers))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ClassScheduleConfiguration : IEntityTypeConfiguration<ClassSchedule>
{
    public void Configure(EntityTypeBuilder<ClassSchedule> builder)
    {
        builder.ToTable("class_schedules");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.DayOfWeek).HasConversion<string>().HasMaxLength(10);
    }
}

public sealed class TeacherAssignmentConfiguration : IEntityTypeConfiguration<TeacherAssignment>
{
    public void Configure(EntityTypeBuilder<TeacherAssignment> builder)
    {
        builder.ToTable("teacher_assignments");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        // One assignment per teacher per class. The aggregate refuses a repeat;
        // this holds if two administrators assign the same teacher at once, and
        // a duplicate would double the teacher count on every screen.
        builder.HasIndex(t => new { t.ClassId, t.TeacherMemberId }).IsUnique();
    }
}
