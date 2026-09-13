import { numericRangeInverted, rangeInverted } from './date-range';

describe('rangeInverted', () => {
  it('is false when both are set in order', () => {
    expect(rangeInverted('2026-09-01T10:00', '2026-09-01T12:00')).toBe(false);
  });

  it('is true when from is after to', () => {
    expect(rangeInverted('2026-09-01T12:00', '2026-09-01T10:00')).toBe(true);
  });

  it('is false when either side is empty or missing - required-ness is a separate check', () => {
    expect(rangeInverted('', '2026-09-01T12:00')).toBe(false);
    expect(rangeInverted('2026-09-01T10:00', '')).toBe(false);
    expect(rangeInverted(null, undefined)).toBe(false);
  });

  it('is false rather than a crash on an unreadable date', () => {
    expect(rangeInverted('not a date', '2026-09-01T12:00')).toBe(false);
  });

  it('is false for two equal instants - equal is not inverted', () => {
    expect(rangeInverted('2026-09-01T10:00', '2026-09-01T10:00')).toBe(false);
  });
});

describe('numericRangeInverted', () => {
  it('is false when min <= max', () => {
    expect(numericRangeInverted(18, 30)).toBe(false);
    expect(numericRangeInverted(18, 18)).toBe(false);
  });

  it('is true when min > max', () => {
    expect(numericRangeInverted(40, 25)).toBe(true);
  });

  it('is false when either side is not set', () => {
    expect(numericRangeInverted(null, 30)).toBe(false);
    expect(numericRangeInverted(18, undefined)).toBe(false);
  });
});
