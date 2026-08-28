using Sangam.IdentityTenant.Domain.Consents;

namespace Sangam.IdentityTenant.Application.Abstractions;

public interface IConsentRepository
{
    /// <summary>Every consent decision this member has made, oldest first.</summary>
    Task<IReadOnlyList<ConsentRecord>> ListForUserAsync(
        Guid userId, CancellationToken cancellationToken = default);

    void Add(ConsentRecord record);
}
