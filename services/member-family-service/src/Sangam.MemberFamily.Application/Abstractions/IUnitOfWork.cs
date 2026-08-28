namespace Sangam.MemberFamily.Application.Abstractions;

/// <summary>
/// Transaction boundary owned by TransactionBehavior. Application code calls
/// SaveChangesAsync; only the behavior begins and commits transactions, so a
/// handler can never leave one open.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IAppTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>True when a transaction is already open on this scope's connection.</summary>
    bool HasActiveTransaction { get; }
}

public interface IAppTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}
