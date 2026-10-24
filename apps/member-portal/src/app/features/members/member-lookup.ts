import { Injectable, inject, signal } from '@angular/core';
import { MembersApi } from './members.api';

/** What a screen shows of a member it found by id. */
export interface MemberCard {
  readonly id: string;
  readonly fullName: string;
  readonly photoUrl: string | null;
}

/**
 * Members named by id on screen - a business's owner, a competition's
 * participants and judges - resolved to a name and photo from
 * member-family-service.
 *
 * Exactly the ids a screen is showing: one `MembersApi.get()` per distinct id,
 * never repeated in a session - the bounded, on-screen lookup this app already
 * uses for a group's roster and a post's author, never a call per member of the
 * Samaaj. Started as the businesses directory's owner lookup (2026-10-24) and
 * moved here when competitions needed the same.
 *
 * A member the reader may not see, or one the lookup fails for, is `null`, and
 * the screen says "A member" rather than printing an id. `undefined` is "still
 * asking".
 */
@Injectable({ providedIn: 'root' })
export class MemberLookup {
  private readonly members = inject(MembersApi);
  private readonly known = signal<ReadonlyMap<string, MemberCard | null>>(new Map());
  private readonly asked = new Set<string>();

  /** Asks for every id not asked for already. */
  resolve(ids: Iterable<string>): void {
    for (const id of new Set(ids)) {
      if (this.asked.has(id)) {
        continue;
      }

      this.asked.add(id);

      this.members.get(id).subscribe({
        next: (member) => this.remember(id, { id, fullName: member.fullName, photoUrl: member.photoUrl }),
        error: () => this.remember(id, null),
      });
    }
  }

  member(id: string): MemberCard | null | undefined {
    return this.known().get(id);
  }

  private remember(id: string, member: MemberCard | null): void {
    this.known.update((current) => new Map(current).set(id, member));
  }
}

/** "SS" for "Shah Sweets" - a business, a competition or a person with no photo yet. */
export function initialsOf(name: string): string {
  return name
    .split(/\s+/)
    .filter((part) => part.length > 0)
    .slice(0, 2)
    .map((part) => part[0]!.toUpperCase())
    .join('');
}
