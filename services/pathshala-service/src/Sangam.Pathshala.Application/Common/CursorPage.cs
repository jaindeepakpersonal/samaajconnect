namespace Sangam.Pathshala.Application.Common;

/// <summary>
/// Cursor pagination envelope. Cursors rather than offsets because tenant
/// directories and feeds are appended to constantly, and offset paging skips
/// or repeats rows when the underlying set shifts between pages.
/// </summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor)
{
    public bool HasMore => NextCursor is not null;

    public static CursorPage<T> Empty() => new([], null);
}
