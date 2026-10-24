import { Component, input } from '@angular/core';
import { AuthedImageDirective } from '@samaajconnect/shared';

/**
 * The top of a profile page, like a social profile's: a cover band, the
 * member's photo over it (their initials when there is none, or this viewer may
 * not see it), their name with Gautra, and a line of what they do and where.
 *
 * Shared by My Profile and a member's page, so the two read as one kind of
 * page - the difference between them is the action on the right (Edit profile
 * on your own), projected in with `[actions]`.
 */
@Component({
  selector: 'app-profile-header',
  imports: [AuthedImageDirective],
  styleUrl: './members.css',
  template: `
    <div class="profile-cover" aria-hidden="true"></div>

    <header class="profile-head">
      @if (photoUrl(); as path) {
        <img class="profile-avatar" [scAuthedSrc]="path" [alt]="'Photo of ' + name()" />
      } @else {
        <span class="profile-avatar initials" aria-hidden="true">{{ initials() }}</span>
      }

      <div class="profile-head-text">
        <h1 class="page-title">
          {{ name() }}
          @if (gautra(); as known) {
            <span class="profile-gautra">({{ known }})</span>
          }
        </h1>
        @if (details().length > 0) {
          <p class="subtitle">{{ details().join(' · ') }}</p>
        }
      </div>

      <div class="profile-actions">
        <ng-content select="[actions]" />
      </div>
    </header>
  `,
})
export class ProfileHeaderComponent {
  readonly name = input.required<string>();
  readonly gautra = input<string | null>(null);
  readonly photoUrl = input<string | null>(null);

  /** Profession, locality and the like - whatever is known, already resolved to words. */
  readonly details = input<readonly string[]>([]);

  initials(): string {
    return this.name()
      .split(/\s+/)
      .filter((part) => part.length > 0)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join('');
  }
}
