namespace Sangam.SocialIssues.Application.Abstractions;

/// <summary>
/// This service's copy of who sits on each Samaaj's Panch. See
/// <see cref="Domain.Panel.PanelMember"/>.
/// </summary>
public interface IPanelRepository
{
    /// <summary>Whether this member sits on the current Samaaj's Panch. Tenant-filtered.</summary>
    Task<bool> IsSeatedAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Whether the current Samaaj has any Panch seated at all. Tenant-filtered.</summary>
    Task<bool> AnySeatedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a Samaaj's rows match <paramref name="memberIds"/> exactly.
    /// </summary>
    /// <remarks>
    /// For the panel consumer, and the reason it takes an explicit tenant: a
    /// consumer resolves no tenant, so the global query filter would compare
    /// against <c>Guid.Empty</c> and match nothing - a roster that silently
    /// never changes. The implementation ignores the filter and applies the
    /// tenant from the event by hand, the same as the erasure path.
    ///
    /// Returns whether anything changed, so redelivery of the same snapshot is
    /// a no-op the handler can report as such.
    /// </remarks>
    Task<bool> ReplaceAsync(
        Guid tenantId, IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default);
}
