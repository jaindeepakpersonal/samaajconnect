using MediatR;

namespace Sangam.SocialIssues.Application.Common;

/// <summary>
/// Marker for a state-changing request. TransactionBehavior keys off this
/// interface to decide whether to open a transaction, which is why commands
/// never implement IRequest&lt;T&gt; directly (CLAUDE.md §4.2).
/// </summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

/// <summary>Marker for a read-only request. Skips TransactionBehavior.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;
