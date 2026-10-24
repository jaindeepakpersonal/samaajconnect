using Sangam.Businesses.Domain.Businesses;

namespace Sangam.Businesses.Application.Businesses;

public static class BusinessMappings
{
    public static ProductResponse ToResponse(this Product product, Guid businessId) =>
        new(product.Id, product.Name, product.Description, product.PriceInPaise, product.IsAvailable,
            product.PhotoId is { } photoId ? $"/v1/businesses/{businessId}/products/{product.Id}/photo?v={photoId:N}" : null);

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
            .Select(p => p.ToResponse(business.Id))],
        business.CreatedAt,
        business.UpdatedAt,
        business.AddressLine,
        business.PhotoId is { } photoId ? $"/v1/businesses/{business.Id}/photo?v={photoId:N}" : null);
}
