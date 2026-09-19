namespace Sangam.MemberFamily.Application.Common;

/// <summary>
/// A page of a server-paginated list: the rows for this page plus whether
/// there is another one. Deliberately no total count - that would cost a
/// second `COUNT(*)` on every call, for a number nothing consuming this
/// currently needs. A handler gets `HasMore` for free by asking its
/// repository for one row more than `Take` and slicing the extra one off
/// before it reaches here.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, bool HasMore);
