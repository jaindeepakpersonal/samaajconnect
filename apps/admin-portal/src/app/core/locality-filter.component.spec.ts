import { ALL_LOCALITIES, WHOLE_SAMAAJ, matchesLocality } from './locality-filter.component';

describe('matchesLocality', () => {
  it('lets everything through for All', () => {
    expect(matchesLocality(ALL_LOCALITIES, 'loc1')).toBe(true);
    expect(matchesLocality(ALL_LOCALITIES, null)).toBe(true);
  });

  it('keeps only rows with no locality for the whole Samaaj', () => {
    expect(matchesLocality(WHOLE_SAMAAJ, null)).toBe(true);
    expect(matchesLocality(WHOLE_SAMAAJ, undefined)).toBe(true);
    expect(matchesLocality(WHOLE_SAMAAJ, 'loc1')).toBe(false);
  });

  it('keeps one locality exactly', () => {
    expect(matchesLocality('loc1', 'loc1')).toBe(true);
    expect(matchesLocality('loc1', 'loc2')).toBe(false);
    expect(matchesLocality('loc1', null)).toBe(false);
  });
});
