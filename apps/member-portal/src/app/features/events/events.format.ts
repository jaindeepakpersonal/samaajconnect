import { SamaajEvent } from './events.models';

/**
 * Date and time formatting for the event screens.
 *
 * Kept in the feature rather than in `libs/shared`. Timeline formats a relative
 * past ("2h ago") and these format an absolute future ("05 Sep 2026 • 6:00 PM")
 * - the same word for two different jobs. A shared helper moves when a third
 * screen wants one of these two, not because two screens both touch dates.
 *
 * All of it goes through `toLocaleDateString`/`toLocaleTimeString` with no
 * locale argument, so it follows the reader's browser. Hard-coding en-IN would
 * be guessing at an audience the platform has not decided about.
 */

/** "05 Sep 2026", or an empty string for anything unparseable. */
export function formatDate(iso: string | null): string {
  const date = parse(iso);

  return date === null
    ? ''
    : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
}

/** "6:00 PM". */
export function formatTime(iso: string | null): string {
  const date = parse(iso);

  return date === null
    ? ''
    : date.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
}

/**
 * The detail screen's subtitle: date, time, venue and who is running it,
 * skipping whatever is absent rather than printing an empty separator.
 */
export function describeWhen(event: SamaajEvent): string {
  return [
    formatDate(event.startAt),
    formatTime(event.startAt),
    event.venue,
    event.organizerType === 'VolunteerGroup'
      ? 'Organised by a volunteer group'
      : 'Organised by the Samaaj',
  ]
    .filter((part): part is string => typeof part === 'string' && part.length > 0)
    .join(' • ');
}

/** Whether the event has already happened, as of now. */
export function hasPassed(event: SamaajEvent, now: number = Date.now()): boolean {
  const end = parse(event.endAt) ?? parse(event.startAt);

  return end !== null && end.getTime() < now;
}

function parse(iso: string | null): Date | null {
  if (iso === null) {
    return null;
  }

  const date = new Date(iso);

  return Number.isNaN(date.getTime()) ? null : date;
}
