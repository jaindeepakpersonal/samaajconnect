import { Injectable, inject, signal } from '@angular/core';
import { MembersApi } from '../members/members.api';

/** What a listing shows of the member behind it. */
export interface BusinessOwner {
  readonly id: string;
  readonly fullName: string;
  readonly photoUrl: string | null;
}

/**
 * The members who own the businesses on screen - name and photo, from
 * member-family-service.
 *
 * A business carries its owner's member id and nothing else, so a listing
 * resolves exactly the owners it is showing: one `MembersApi.get()` per
 * distinct id, never repeated in a session - the bounded, on-screen lookup this
 * app already uses for a group's roster and a post's author. The directory
 * answers at most fifty businesses, so this is never a call per member of the
 * Samaaj.
 *
 * An owner the reader may not see, or one the lookup fails for, is `null`, and
 * the screen says "A member" rather than printing an id. `undefined` is "still
 * asking".
 */
@Injectable({ providedIn: 'root' })
export class BusinessOwners {
  private readonly members = inject(MembersApi);
  private readonly known = signal<ReadonlyMap<string, BusinessOwner | null>>(new Map());
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

  owner(id: string): BusinessOwner | null | undefined {
    return this.known().get(id);
  }

  private remember(id: string, owner: BusinessOwner | null): void {
    this.known.update((current) => new Map(current).set(id, owner));
  }
}

/** "SS" for "Shah Sweets" - a business or person with no photo yet. */
export function initialsOf(name: string): string {
  return name
    .split(/\s+/)
    .filter((part) => part.length > 0)
    .slice(0, 2)
    .map((part) => part[0]!.toUpperCase())
    .join('');
}
