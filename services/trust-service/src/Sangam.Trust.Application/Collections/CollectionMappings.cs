namespace Sangam.Trust.Application.Collections;

// Referenced as `Domain.Collections.Collection`, never bare - see that
// type's own remarks.

internal static class CollectionMappings
{
    public static CollectionResponse ToResponse(this Domain.Collections.Collection collection) => new(
        collection.Id,
        collection.TrustId,
        collection.SourceType.ToString(),
        collection.SourceId,
        collection.PayerMemberId,
        collection.Amount,
        collection.Status.ToString(),
        collection.Mode,
        collection.CollectedBy,
        collection.CollectedAt,
        collection.CreatedAt);
}
