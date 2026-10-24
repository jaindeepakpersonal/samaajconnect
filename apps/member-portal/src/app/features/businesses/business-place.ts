import { City, Locality } from '../members/members.models';

/**
 * Where a business is, as one line: its own street line, then the locality and
 * the city or village, each only if known - "Shop 4, Bapu Bazaar · Hiran Magri,
 * Udaipur". Null when nothing is.
 *
 * Resolved against the City and Locality lists a screen loads once, never a
 * call per business; an id that does not resolve is left out rather than
 * printed.
 */
export function placeOf(
  business: { readonly addressLine: string | null; readonly cityId: string | null; readonly localityId: string | null },
  cities: readonly City[],
  localities: readonly Locality[],
): string | null {
  const area = [
    localities.find((l) => l.id === business.localityId)?.name,
    cities.find((c) => c.id === business.cityId)?.name,
  ].filter((part): part is string => !!part).join(', ');

  const parts = [business.addressLine, area].filter((part): part is string => !!part && part.trim().length > 0);

  return parts.length > 0 ? parts.join(' · ') : null;
}
