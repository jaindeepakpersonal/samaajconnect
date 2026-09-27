using Sangam.Pathshala.Application.Abstractions;
using Sangam.Pathshala.Application.Common;
using Sangam.Pathshala.Domain.Enrolments;
using Sangam.Pathshala.Domain.Pathshalas;

namespace Sangam.Pathshala.Application.Security;

/// <summary>
/// Who may act on a Pathshala, a class, or one child's records.
/// </summary>
/// <remarks>
/// The permissions on the commands answer "is this caller a teacher or an
/// administrator at all". They cannot answer "of <i>this</i> class", and
/// treating them as though they did would let any teacher in the Samaaj mark
/// any register in any Pathshala. So every write and every student-facing read
/// pairs its permission with one of these data checks - the pattern
/// volunteer-groups-service established with "are you this group's president?".
///
/// Refusals are <b>not found</b> rather than forbidden, throughout. A 403 on a
/// class id confirms the class exists; a parent probing enrolment ids should
/// not be able to map out a Pathshala's roster by the difference between two
/// error codes.
/// </remarks>
/// <summary>
/// A staff member's view of a Pathshala: whether they run it, and which classes'
/// records they may read.
/// </summary>
public sealed record StaffScope(bool IsAdministrator, IReadOnlySet<Guid> ClassIds);

public static class PathshalaAccess
{
    public static readonly Error NoSuchPathshala =
        Error.NotFound("Pathshala.NotFound", "No such Pathshala in this Samaaj.");

    public static readonly Error NoSuchClass =
        Error.NotFound("Class.NotFound", "No such class in this Samaaj.");

    public static readonly Error NoSuchEnrolment =
        Error.NotFound("Enrolment.NotFound", "No such enrolment in this Samaaj.");

    public static readonly Error NoSuchExam =
        Error.NotFound("Exam.NotFound", "No such exam in this Samaaj.");

    /// <summary>
    /// Whether the Pathshala belongs to the Samaaj this request is scoped to.
    /// </summary>
    /// <remarks>
    /// The IDOR guard root CLAUDE.md section 6 requires on every write path.
    /// The global query filter should already have excluded it; this is the
    /// check that does not depend on the filter being right.
    /// </remarks>
    public static bool IsInTenant(this Domain.Pathshalas.Pathshala? pathshala, Guid? tenantId) =>
        pathshala is not null && (tenantId is not { } id || pathshala.TenantId == id);

    public static bool IsInTenant(this StudentEnrolment? enrolment, Guid? tenantId) =>
        enrolment is not null && (tenantId is not { } id || enrolment.TenantId == id);

    public static bool IsInTenant(this Exam? exam, Guid? tenantId) =>
        exam is not null && (tenantId is not { } id || exam.TenantId == id);

    /// <summary>
    /// Whether this caller may run <paramref name="pathshala"/> - open sessions,
    /// create classes, place students.
    /// </summary>
    /// <remarks>
    /// The permission, and - for an Area Admin - the Pathshala being in their
    /// own locality. A Pathshala with no locality is the Samaaj Admin's alone.
    /// </remarks>
    public static bool CanAdminister(
        ICurrentUser currentUser, IAreaScope area, Domain.Pathshalas.Pathshala pathshala) =>
        currentUser.HasPermission(PermissionKeys.PathshalaManage) && pathshala.IsAdministeredBy(area);

    /// <summary>
    /// The locality half of <see cref="CanAdminister"/>, for the commands whose
    /// permission attribute has already answered the other half. False for a
    /// null Pathshala, so it reads as one more "not found" beside
    /// <c>IsInTenant</c>.
    /// </summary>
    public static bool IsAdministeredBy(this Domain.Pathshalas.Pathshala? pathshala, IAreaScope area) =>
        pathshala is not null && area.MayAdminister(pathshala.Address?.LocalityId);

    /// <summary>
    /// Whether this caller may mark <paramref name="pathshalaClass"/>'s
    /// register or record its results.
    /// </summary>
    /// <remarks>
    /// A Pathshala administrator can, because somebody has to be able to when a
    /// teacher is away. Otherwise it is the teachers of this class and nobody
    /// else - holding the permission is necessary and not sufficient.
    /// </remarks>
    public static bool CanTeach(
        ICurrentUser currentUser,
        IAreaScope area,
        Domain.Pathshalas.Pathshala pathshala,
        PathshalaClass pathshalaClass,
        string permission) =>
        CanAdminister(currentUser, area, pathshala)
        || (currentUser.HasPermission(permission)
            && currentUser.UserId is { } userId
            && pathshala.Teaches(userId, pathshalaClass));

    /// <summary>
    /// What this caller sees of a Pathshala as its staff: every class if they
    /// run it, the classes they teach if they are one of its teachers, and null
    /// if neither - which each staff read answers as "not found".
    /// </summary>
    /// <remarks>
    /// The Students, Progress, Attendance and Exams tabs are all "rows for the
    /// classes this caller may see", so they share this one answer rather than
    /// four copies of "administrator, or teacher of which classes?".
    /// </remarks>
    /// <summary>
    /// A caller who is not a member of the Samaaj: a Pathshala teacher from
    /// outside it, whose account holds Pathshala.Teach and not Members.Read.
    /// They see the Pathshalas they teach and nothing else - never the
    /// directory a member browses.
    /// </summary>
    public static bool IsOutsider(ICurrentUser currentUser) =>
        !currentUser.HasPermission(PermissionKeys.MembersRead);

    public static StaffScope? StaffScopeOf(
        ICurrentUser currentUser, IAreaScope area, Domain.Pathshalas.Pathshala pathshala)
    {
        if (CanAdminister(currentUser, area, pathshala))
        {
            return new StaffScope(true, pathshala.Classes.Select(c => c.Id).ToHashSet());
        }

        if (currentUser.UserId is { } userId && pathshala.TeacherFor(userId) is { } teacher)
        {
            return new StaffScope(
                false,
                pathshala.Classes.Where(c => c.HasTeacher(teacher.Id)).Select(c => c.Id).ToHashSet());
        }

        return null;
    }

    /// <summary>
    /// Whether this caller may read one child's attendance, exams or progress.
    /// </summary>
    /// <remarks>
    /// Four ways in, and no role among them. The parent who asked for the
    /// place; the student themselves once conversion has given them an account;
    /// a teacher of the class they were placed in; and a Pathshala
    /// administrator. See PermissionKeys for why <c>PathshalaStudent</c> is not
    /// one of them.
    /// </remarks>
    public static bool CanReadRecordsOf(
        ICurrentUser currentUser,
        IAreaScope area,
        StudentEnrolment enrolment,
        Domain.Pathshalas.Pathshala? pathshala)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return false;
        }

        if (enrolment.BelongsTo(memberId)
            || (pathshala is not null && CanAdminister(currentUser, area, pathshala)))
        {
            return true;
        }

        var pathshalaClass = enrolment.ClassId is { } classId
            ? pathshala?.FindClass(classId)
            : null;

        return pathshalaClass is not null && pathshala!.Teaches(memberId, pathshalaClass);
    }
}
