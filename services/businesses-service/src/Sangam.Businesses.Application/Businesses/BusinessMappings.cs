using Sangam.Businesses.Domain.Businesses;

namespace Sangam.Businesses.Application.Businesses;

public static class BusinessMappings
{
    public static ProductResponse ToResponse(this Product product) =>
        new(product.Id, product.Name, product.Description, product.PriceInPaise, product.IsAvailable);

    /// <summary>
    /// <paramref name="onlyAvailableProducts"/> is true for anyone but the
    /// owner - a directory viewer should not be offered to order a product
    /// the owner has taken off sale, and should not even see that it exists.
    /// </summary>
    public static BusinessResponse ToResponse(this Business business, bool onlyAvailableProducts) => new(
        business.Id,
        business.OwnerMemberId,
        business.Name,
        business.CategoryId,
        business.CityId,
        business.LocalityId,
        business.Description,
        business.ContactPhone,
        business.ContactEmail,
        business.IsActive,
        [.. business.Products
            .Where(p => !onlyAvailableProducts || p.IsAvailable)
            .Select(p => p.ToResponse())],
        business.CreatedAt,
        business.UpdatedAt);
}
