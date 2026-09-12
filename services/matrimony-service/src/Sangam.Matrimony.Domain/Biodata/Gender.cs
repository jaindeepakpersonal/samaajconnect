namespace Sangam.Matrimony.Domain.Biodata;

/// <summary>
/// Mirrors member-family-service's own `Gender`. A local copy on purpose -
/// consuming another service's type would couple the two deployments
/// together, the same reasoning member-family-service's own
/// `UserRegisteredPayload` mirror already documents.
/// </summary>
public enum Gender
{
    Unspecified = 0,
    Female = 1,
    Male = 2,
    Other = 3,
}
