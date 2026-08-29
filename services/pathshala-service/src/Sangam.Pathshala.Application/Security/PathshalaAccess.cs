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
    /// Whether this caller may run the Pathshala - open sessions, create
    /// classes, place students.
    /// </summary>
    public static bool CanAdminister(ICurrentUser currentUser) =>
        currentUser.HasPermission(PermissionKeys.PathshalaManage);

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
        ICurrentUser currentUser, PathshalaClass pathshalaClass, string permission) =>
        CanAdminister(currentUser)
        || (currentUser.HasPermission(permission)
            && currentUser.UserId is { } memberId
            && pathshalaClass.IsTaughtBy(memberId));

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
        StudentEnrolment enrolment,
        Domain.Pathshalas.Pathshala? pathshala)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return false;
        }

        if (enrolment.BelongsTo(memberId) || CanAdminister(currentUser))
        {
            return true;
        }

        var pathshalaClass = enrolment.ClassId is { } classId
            ? pathshala?.FindClass(classId)
            : null;

        return pathshalaClass is not null && pathshalaClass.IsTaughtBy(memberId);
    }
}
