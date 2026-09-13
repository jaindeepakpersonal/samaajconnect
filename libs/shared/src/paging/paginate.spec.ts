import { DEFAULT_PAGE_SIZE, paginate } from './paginate';

describe('paginate', () => {
  it('slices the requested page at the default size of 20', () => {
    const all = Array.from({ length: 45 }, (_, i) => i);

    expect(paginate(all, 1).items).toEqual(all.slice(0, 20));
    expect(paginate(all, 2).items).toEqual(all.slice(20, 40));
    expect(paginate(all, 3).items).toEqual(all.slice(40, 45));
    expect(DEFAULT_PAGE_SIZE).toBe(20);
  });

  it('reports a page count of at least 1 even for an empty list', () => {
    const result = paginate([], 1);

    expect(result.pageCount).toBe(1);
    expect(result.items).toEqual([]);
    expect(result.total).toBe(0);
  });

  it('clamps a page past the end back onto the last real page', () => {
    // A pageSignal can point past the end after a filter narrows the list -
    // silently returning zero rows would read as a bug, not as the count
    // shrinking.
    const all = Array.from({ length: 25 }, (_, i) => i);

    const result = paginate(all, 99);

    expect(result.page).toBe(2);
    expect(result.items).toEqual(all.slice(20, 25));
  });

  it('clamps a page below 1 up to the first page', () => {
    const all = [1, 2, 3];

    expect(paginate(all, 0).page).toBe(1);
    expect(paginate(all, -5).page).toBe(1);
  });

  it('honours a custom page size', () => {
    const all = Array.from({ length: 10 }, (_, i) => i);

    const result = paginate(all, 1, 4);

    expect(result.items).toEqual([0, 1, 2, 3]);
    expect(result.pageCount).toBe(3);
  });
});
