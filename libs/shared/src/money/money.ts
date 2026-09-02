/**
 * Money, in the one place it is allowed to be converted.
 *
 * boli-service holds every amount as an integer number of paise, deliberately:
 * a Boli is money the Samaaj announces and collects against, and a
 * floating-point field accumulates error that shows up as a winning bid a rupee
 * off what somebody actually offered. The portal has to be as careful, which is
 * why the conversion lives here rather than being inlined at each call site.
 */

/** How many paise make a rupee. Named so the arithmetic below reads. */
const PAISE_PER_RUPEE = 100;

/**
 * Formats paise as rupees, grouped the Indian way — ₹15,100 not ₹15,100 by
 * thousands, and ₹1,50,000 for a lakh and a half.
 *
 * Explicitly `en-IN` rather than the browser's locale: the grouping is a fact
 * about the amount's own convention here, not about who is reading it, and a
 * member on a US-locale phone should still see the number their Samaaj wrote.
 */
export function formatRupees(paise: number): string {
  return new Intl.NumberFormat('en-IN', {
    style: 'currency',
    currency: 'INR',
    // Whole rupees unless there are actual paise. Bids are almost always round
    // numbers, and "₹15,100.00" is noise on every one of them.
    minimumFractionDigits: paise % PAISE_PER_RUPEE === 0 ? 0 : 2,
    maximumFractionDigits: 2,
  }).format(paise / PAISE_PER_RUPEE);
}

/**
 * Turns what a member typed into paise, or null if it is not an amount.
 *
 * **Rounds rather than truncating, and rounds after multiplying.** `15600.50`
 * parsed as a float and multiplied by 100 is 1560050.0000000002, and `Math.trunc`
 * on that is still right — but `15600.07` gives 1560006.9999999998, which
 * truncates to a paisa less than the member typed. Rounding is what makes the
 * number they see the number that is sent.
 *
 * Accepts the separators people actually type: a leading ₹, commas from
 * pasting, and surrounding space.
 */
export function parseRupees(input: string): number | null {
  const cleaned = input.trim().replace(/^₹/, '').replace(/,/g, '').trim();

  if (cleaned.length === 0) {
    return null;
  }

  // Not parseFloat: it accepts "12abc" as 12, which would silently bid an
  // amount nobody typed.
  if (!/^\d+(\.\d{1,2})?$/.test(cleaned)) {
    return null;
  }

  const rupees = Number(cleaned);

  if (!Number.isFinite(rupees)) {
    return null;
  }

  return Math.round(rupees * PAISE_PER_RUPEE);
}

/**
 * The plain-number string to put in an input for a given amount in paise.
 *
 * No grouping and no symbol: this goes into a text field the member may edit,
 * and a value the field cannot parse back is worse than an unformatted one.
 */
export function toInputValue(paise: number): string {
  return paise % PAISE_PER_RUPEE === 0
    ? String(paise / PAISE_PER_RUPEE)
    : (paise / PAISE_PER_RUPEE).toFixed(2);
}

/**
 * How long until a Boli closes, in words a bidder can act on.
 *
 * The wireframe says "Bidding closes 6:00 PM today". A time alone is the wrong
 * unit when the answer that matters is "do I have minutes or days", so this
 * leads with the distance and lets the screen print the exact time beside it.
 */
export function closesIn(endAt: string, now: Date = new Date()): string {
  const end = new Date(endAt);

  if (Number.isNaN(end.getTime())) {
    return '';
  }

  const seconds = Math.round((end.getTime() - now.getTime()) / 1000);

  if (seconds <= 0) {
    return 'Bidding has closed';
  }

  const minutes = Math.floor(seconds / 60);

  if (minutes < 1) {
    return 'Closes in under a minute';
  }

  if (minutes < 60) {
    return `Closes in ${minutes} ${minutes === 1 ? 'minute' : 'minutes'}`;
  }

  const hours = Math.floor(minutes / 60);

  if (hours < 24) {
    return `Closes in ${hours} ${hours === 1 ? 'hour' : 'hours'}`;
  }

  const days = Math.floor(hours / 24);

  return `Closes in ${days} ${days === 1 ? 'day' : 'days'}`;
}
