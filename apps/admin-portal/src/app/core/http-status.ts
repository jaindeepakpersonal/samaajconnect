/**
 * The two HTTP statuses this panel treats as answers rather than errors.
 *
 * Both were written out three times across three features before landing here.
 * A copy of a predicate is not dangerous on its own; three copies of one that
 * decides whether a screen shows "the module is off" or a red error banner is
 * three places to fix when the shape of a failure changes.
 */

/** Narrowing helper: an error object carrying a numeric `status`. */
function statusOf(failure: unknown): number | null {
  if (typeof failure !== 'object' || failure === null || !('status' in failure)) {
    return null;
  }

  const status = (failure as { status: unknown }).status;

  return typeof status === 'number' ? status : null;
}

/**
 * A 404.
 *
 * On a module-gated route this means the module is **off for this Samaaj**, not
 * that something broke: the gateway answers 404 so that a Samaaj without a
 * module is indistinguishable from a platform with no such feature. Reporting
 * it as an error sends an administrator hunting a bug that is a setting.
 */
export function isNotFound(failure: unknown): boolean {
  return statusOf(failure) === 404;
}

/**
 * A 403.
 *
 * The caller is authenticated and the thing exists; they simply may not do
 * this. Worth distinguishing from an empty answer wherever a screen would
 * otherwise say "there is nothing here" to somebody who is not allowed to look
 * — the Boli publication queue being the case that prompted this.
 */
export function isForbidden(failure: unknown): boolean {
  return statusOf(failure) === 403;
}
