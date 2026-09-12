namespace Sangam.Matrimony.Domain.Biodata;

/// <summary>
/// A small, fixed, platform-wide list - not a master-data-service
/// `LookupItem`, the same reasoning `Gender` gets rather than being made an
/// admin-managed category. Nobody needs to tenant-customize what "diet"
/// means.
/// </summary>
public enum Diet
{
    Vegetarian = 0,
    JainVegetarian = 1,
    Vegan = 2,
    NonVegetarian = 3,
}
