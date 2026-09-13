/**
 * "From ≤ to" checks, shared so every date-pair and number-pair field on
 * this platform uses the same rule instead of a hand-written comparison per
 * screen. Neither function is required-ness: an empty side is not an error
 * here, because a field being blank is a separate, usually stricter check a
 * screen already makes (or should make) before this one runs.
 */

/**
 * True when `from` and `to` are both set and `from` is after `to` - a
 * datetime-local or date input pair rendered out of order.
 */
export function rangeInverted(
  from: string | null | undefined,
  to: string | null | undefined,
): boolean {
  if (!from || !to) {
    return false;
  }

  const fromTime = new Date(from).getTime();
  const toTime = new Date(to).getTime();

  if (Number.isNaN(fromTime) || Number.isNaN(toTime)) {
    return false;
  }

  return fromTime > toTime;
}

/** The same rule for a plain numeric range - an age, a height, a count. */
export function numericRangeInverted(
  min: number | null | undefined,
  max: number | null | undefined,
): boolean {
  if (min === null || min === undefined || max === null || max === undefined) {
    return false;
  }

  return min > max;
}
