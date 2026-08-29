namespace Sangam.Pathshala.Application.Security;

/// <summary>Seeded platform roles (DATA-MODEL.md §2).</summary>
public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string SamaajAdmin = "SamaajAdmin";
    public const string Member = "Member";
    public const string FamilyHead = "FamilyHead";
    public const string VolunteerGroupPresident = "VolunteerGroupPresident";
    public const string PathshalaTeacher = "PathshalaTeacher";
    public const string PathshalaStudent = "PathshalaStudent";
    public const string ContentModerator = "ContentModerator";
    public const string BoliManager = "BoliManager";
}
