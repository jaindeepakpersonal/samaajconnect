namespace Sangam.Pathshala.Application.Security;

/// <summary>
/// Permission keys this service gates on, in the platform's {Module}.{Action}
/// convention (SECURITY-CHECKLIST.md). Minted by identity-tenant-service and
/// arriving as token claims.
/// </summary>
/// <remarks>
/// <b>There is no <c>PathshalaStudent</c> gate here, deliberately.</b> The role
/// exists in the platform catalogue and nothing grants it: it was described as
/// "created by enrolment", but enrolment happens in this service and this
/// service cannot write role grants in identity-tenant-service. A permission
/// held only by a role nobody has is a permission nobody has - the fourth time
/// that shape has bitten this repo, after FamilyHead and
/// VolunteerGroupPresident.
///
/// So the student-facing views are gated on <see cref="MembersRead"/>, which
/// every signed-in member holds, and who may actually read a given record is
/// decided against the data: the parent who asked for the place, the student
/// once they have their own account, the teacher of that class, or somebody
/// holding <see cref="PathshalaManage"/>. That is a stronger check than a role
/// claim, and it is the same shape volunteer-groups-service and
/// member-family-service already use.
/// </remarks>
public static class PermissionKeys
{
    /// <summary>
    /// Run a Pathshala: open sessions, create classes, assign teachers, place
    /// and withdraw students. Samaaj admins hold it.
    /// </summary>
    /// <remarks>
    /// Creating the master record needs this <i>and</i> the SuperAdmin role -
    /// DATA-MODEL.md section 9 reserves that one act to the platform.
    /// Everything else is the Samaaj's to run.
    /// </remarks>
    public const string PathshalaManage = "Pathshala.Manage";

    /// <summary>Mark a register. Teachers hold it.</summary>
    /// <remarks>
    /// Says somebody is a teacher; says nothing about <i>whose</i> register they
    /// may mark. That is checked against the class - see
    /// <c>PathshalaClass.IsTaughtBy</c> - because otherwise any teacher in the
    /// Samaaj could mark any class in any Pathshala.
    /// </remarks>
    public const string PathshalaAttendanceWrite = "Pathshala.Attendance.Write";

    /// <summary>Set exams and record results. Teachers hold it, with the same caveat.</summary>
    public const string PathshalaExamsWrite = "Pathshala.Exams.Write";

    /// <summary>
    /// Ask for a place, and read your own child's records. Every signed-in
    /// member holds it.
    /// </summary>
    public const string MembersRead = "Members.Read";
}
