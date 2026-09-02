import { closesIn, formatRupees, parseRupees, toInputValue } from './money';

/**
 * Money, in the one place this platform converts it.
 *
 * These assertions lived in the member portal's Boli spec, which is where the
 * code lived until the admin panel grew a screen that opens a Boli. They moved
 * here with it: a shared module tested only from one of the two apps that use
 * it is a module whose contract is described in the wrong place, and the
 * consequence of getting any of this wrong is a winning bid that differs from
 * what somebody actually offered, in a record the Samaaj collects against.
 */
describe('formatRupees', () => {
  it('groups the Indian way, not by thousands', () => {
    expect(formatRupees(1_510_000)).toContain('15,100');
    expect(formatRupees(15_000_000)).toContain('1,50,000');
  });

  it('groups en-IN whatever locale the reader is on', () => {
    // Explicitly en-IN rather than the browser's locale: the grouping is a fact
    // about the amount's own convention, not about who is reading it, so a
    // member on a US-locale phone still sees the number their Samaaj wrote.
    expect(formatRupees(15_000_000)).not.toContain('150,000');
  });

  it('does not print paise on a round amount', () => {
    expect(formatRupees(1_510_000)).not.toContain('.00');
  });

  it('prints paise when there are any', () => {
    expect(formatRupees(1_510_050)).toContain('15,100.50');
  });

  it('formats zero as an amount rather than as nothing', () => {
    expect(formatRupees(0)).toContain('0');
  });
});

describe('parseRupees', () => {
  it('rounds rather than truncating, and after multiplying', () => {
    // 15600.07 as a float times 100 is 1560006.9999999998. Truncating that
    // takes a paisa off what the member typed - and 15600.50 is the case that
    // makes a naive fix look fine, because it happens to land exactly.
    expect(parseRupees('15600.07')).toBe(1_560_007);
    expect(parseRupees('15600.50')).toBe(1_560_050);
  });

  it('accepts what people actually type', () => {
    expect(parseRupees('  15600 ')).toBe(1_560_000);
    expect(parseRupees('₹15,600')).toBe(1_560_000);
    expect(parseRupees('₹ 1,50,000')).toBe(15_000_000);
  });

  it('refuses anything that is not an amount', () => {
    // parseFloat reads "12abc" as 12, and bidding a number nobody typed is the
    // worst possible way to be lenient.
    expect(parseRupees('12abc')).toBeNull();
    expect(parseRupees('')).toBeNull();
    expect(parseRupees('   ')).toBeNull();
    expect(parseRupees('-500')).toBeNull();
    expect(parseRupees('15600.123')).toBeNull();
    expect(parseRupees('1e5')).toBeNull();
    expect(parseRupees('.5')).toBeNull();
  });

  it('reads zero as zero and not as nothing', () => {
    // Distinct from null: a Boli floor of zero is refused by the service as a
    // decision, which it can only do if the client sends the zero it was given.
    expect(parseRupees('0')).toBe(0);
  });
});

describe('toInputValue', () => {
  it('round-trips an amount through the input and back', () => {
    expect(parseRupees(toInputValue(1_560_050))).toBe(1_560_050);
    expect(parseRupees(toInputValue(1_560_000))).toBe(1_560_000);
    expect(parseRupees(toInputValue(0))).toBe(0);
  });

  it('writes no grouping and no symbol', () => {
    // This goes into a field the member may edit, and a value the field cannot
    // parse back is worse than an unformatted one.
    expect(toInputValue(15_000_000)).toBe('150000');
    expect(toInputValue(1_560_050)).toBe('15600.50');
  });
});

describe('closesIn', () => {
  const now = new Date('2026-09-01T05:00:00Z');

  it('leads with the distance rather than the time', () => {
    // "Bidding closes 6:00 PM today" is the wrong unit when the question is
    // whether the bidder has minutes or days.
    expect(closesIn('2026-09-01T05:30:00Z', now)).toBe('Closes in 30 minutes');
    expect(closesIn('2026-09-01T08:00:00Z', now)).toBe('Closes in 3 hours');
    expect(closesIn('2026-09-04T05:00:00Z', now)).toBe('Closes in 3 days');
  });

  it('says so once it has closed', () => {
    expect(closesIn('2026-09-01T04:00:00Z', now)).toBe('Bidding has closed');
    expect(closesIn('2026-09-01T05:00:00Z', now)).toBe('Bidding has closed');
  });

  it('does not round the last minute away', () => {
    expect(closesIn('2026-09-01T05:00:30Z', now)).toBe('Closes in under a minute');
  });

  it('uses the singular where there is one of something', () => {
    expect(closesIn('2026-09-01T05:01:00Z', now)).toBe('Closes in 1 minute');
    expect(closesIn('2026-09-01T06:00:00Z', now)).toBe('Closes in 1 hour');
    expect(closesIn('2026-09-02T05:00:00Z', now)).toBe('Closes in 1 day');
  });

  it('says nothing rather than something wrong about an unreadable date', () => {
    expect(closesIn('not a date', now)).toBe('');
  });
});
