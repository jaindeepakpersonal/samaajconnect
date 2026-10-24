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

/**
 * How long an event with no end time is taken to last - the same day
 * events-service uses (`SamaajEvent.UntimedEventLength`), so the list the
 * service sends and the "already happened" this screen draws agree.
 */
export const UNTIMED_EVENT_MS = 24 * 60 * 60 * 1000;

/** Whether the event is over, as of now: past its end, or a day past its start when it has none. */
export function hasPassed(event: SamaajEvent, now: number = Date.now()): boolean {
  const end = parse(event.endAt);

  if (end !== null) {
    return end.getTime() < now;
  }

  const start = parse(event.startAt);

  return start !== null && start.getTime() + UNTIMED_EVENT_MS < now;
}

/** "18:30:00" as the reader's own clock format, e.g. "6:30 PM". Wall-clock: never shifted between zones. */
export function formatClock(time: string): string {
  const [hours = 0, minutes = 0] = time.split(':').map((part) => Number(part));

  return new Date(2000, 0, 1, hours, minutes).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
}

/** A schedule's "2026-11-24" as "Tue, 24 Nov 2026" - read as a local date, not UTC midnight. */
export function formatScheduleDay(date: string): string {
  const [year = 1970, month = 1, day = 1] = date.split('-').map((part) => Number(part));

  return new Date(year, month - 1, day).toLocaleDateString(undefined, {
    weekday: 'short',
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  });
}

function parse(iso: string | null): Date | null {
  if (iso === null) {
    return null;
  }

  const date = new Date(iso);

  return Number.isNaN(date.getTime()) ? null : date;
}
