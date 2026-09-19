/**
 * Client-side paging for a list a screen has already fetched in full.
 *
 * Most lists on this platform are naturally small and come back from their
 * API unbounded — there is nowhere to ask the server for "page 2" of a
 * Samaaj's own lookup categories or its own volunteer groups. Slicing an
 * already-fetched array is enough there; the handful of lists that can
 * genuinely grow large (the member directory, audit logs, the timeline feed)
 * ask the server directly instead, and drive `<app-paginator>` off a
 * `hasMore` flag rather than this function's `pageCount`.
 */

/**
 * A page of a server-paginated list, as the backend's `PagedResult<T>` puts it
 * on the wire. There is no total: the server asks its repository for one row
 * more than a page holds and reports only whether that extra row existed.
 */
export interface Paged<T> {
  items: readonly T[];
  hasMore: boolean;
}

/** Every grid on this platform defaults to this, per the user's own instruction. */
export const DEFAULT_PAGE_SIZE = 20;

export interface Page<T> {
  items: readonly T[];
  /** 1-based, and always the page actually returned — see `clamp` below. */
  page: number;
  /** At least 1, even for an empty list, so "Page 1 of 1" is never "Page 1 of 0". */
  pageCount: number;
  total: number;
}

/**
 * Slices `all` to the requested page, clamping `page` into range first.
 *
 * The clamp matters more than it looks: a screen's `pageSignal` can point
 * past the end after a filter narrows the list or an item is deleted, and
 * silently returning zero rows for a page that no longer exists reads as a
 * bug ("where did everything go?") rather than as the page count shrinking.
 */
export function paginate<T>(
  all: readonly T[],
  page: number,
  pageSize: number = DEFAULT_PAGE_SIZE,
): Page<T> {
  const pageCount = Math.max(1, Math.ceil(all.length / pageSize));
  const clamped = Math.min(Math.max(1, page), pageCount);
  const start = (clamped - 1) * pageSize;

  return {
    items: all.slice(start, start + pageSize),
    page: clamped,
    pageCount,
    total: all.length,
  };
}
