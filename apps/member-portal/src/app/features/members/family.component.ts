import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { MembersApi } from './members.api';
import {
  Child,
  ChildDataNotice,
  Family,
  FamilyMember,
  Genders,
  Relationship,
  Relationships,
} from './members.models';

/**
 * My Family, from the member-portal wireframe's `#family` and `#children`
 * screens.
 *
 * The wireframe splits these across two screens; they are one here because a
 * household and the children in it are one thing to a member, and the children
 * endpoint returns the household's children with no way to ask for anybody
 * else's.
 *
 * **Adding a child shows the data notice first, and will not submit without
 * it.** DPDP section 9 makes parental consent the basis on which a child's data
 * may be held, and section 6(7) means a consent that cannot say what was shown
 * is worth little - so the notice is fetched before the form is offered, its
 * version travels back with the consent, and the tick is never pre-filled. That
 * ordering is the requirement, not a nicety.
 *
 * The wireframe's "Invite" button is not built: there is no notification
 * channel, so an invitation could not be delivered. Joining works the other way
 * round - the head shares the family code, which only they can see.
 */
@Component({
  selector: 'app-family',
  imports: [FormsModule, RouterLink],
  styleUrl: './members.css',
  template: `
    <div class="members-page">
      <header class="page-head">
        <div>
          <h1 class="page-title">My family</h1>
          <p class="subtitle">Create or join a household, and manage the children in it.</p>
        </div>
        <a class="btn secondary" routerLink="/home">Back to home</a>
      </header>

      @if (loading()) {
        <p role="status">Loading your household…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (family(); as household) {
        <div class="grid2">
          <!-- The household ---------------------------------------------- -->
          <div class="card">
            <h3>Your household</h3>

            <div class="badges">
              @if (household.viewerIsHead) {
                <span class="pill ok">Family head</span>
              } @else {
                <span class="pill">Member</span>
              }
              <span class="pill">
                {{ activeMembers(household).length }}
                {{ activeMembers(household).length === 1 ? 'person' : 'people' }}
              </span>
              <span class="pill">
                {{ children().length }}
                {{ children().length === 1 ? 'child' : 'children' }}
              </span>
            </div>

            @if (activeMembers(household).length === 0) {
              <p class="small">Nobody else has joined yet.</p>
            } @else {
              <ul class="people">
                @for (person of activeMembers(household); track person.id) {
                  <li>
                    {{ nameFor(person, household) }}
                    <span class="small">· {{ person.relationship }}</span>
                  </li>
                }
              </ul>
            }
          </div>

          <!-- The code, or the queue ------------------------------------- -->
          <div class="card">
            @if (household.viewerIsHead) {
              <h3>Invite someone</h3>

              <!-- Present only for the head: it is the token anyone needs to
                   request to join, so handing it to every member would let any
                   one of them invite the Samaaj into the household. -->
              @if (household.familyCode; as code) {
                <p class="small">
                  Give this code to somebody in your household. They enter it under
                  "Join a household".
                </p>
                <p class="family-code">{{ code }}</p>
              }

              <p class="small">
                There is no way to send an invitation yet - the platform has no notification
                channel - so the code is passed on in person.
              </p>

              <h3>Requests to join</h3>

              @if (pendingRequests(household).length === 0) {
                <p class="small">Nobody is waiting.</p>
              } @else {
                @for (request of pendingRequests(household); track request.id) {
                  <div class="request">
                    <p>
                      <b>{{ request.fullName }}</b>
                      <span class="small"> · {{ request.relationship }}</span>
                    </p>

                    @if (actionError()[request.id]; as message) {
                      <p class="notice error" role="alert">{{ message }}</p>
                    }

                    <div class="actions">
                      <button
                        class="btn small"
                        type="button"
                        [disabled]="busy()"
                        (click)="decide(household, request, true)"
                      >
                        Accept
                      </button>
                      <button
                        class="btn small secondary"
                        type="button"
                        [disabled]="busy()"
                        (click)="decide(household, request, false)"
                      >
                        Turn down
                      </button>
                    </div>
                  </div>
                }
              }
            } @else {
              <h3>Your household</h3>
              <p class="small">
                Only the family head can invite people or decide requests to join.
              </p>
            }
          </div>
        </div>

        <!-- Children --------------------------------------------------- -->
        <h2 class="section-heading">Children</h2>
        <p class="small">Child profiles stay linked to the household.</p>

        <div class="grid">
          @for (child of children(); track child.id) {
            <div class="card">
              <h3>{{ child.fullName }}</h3>
              <p>{{ date(child.dateOfBirth) }} • Age {{ child.age }}</p>

              @if (child.status === 'Converted') {
                <span class="pill ok">Has their own account</span>
              } @else if (child.hasPendingConversion) {
                <span class="pill warn">Waiting for a Samaaj admin</span>
              } @else if (child.isEligibleForConversion) {
                <div class="notice info" role="status">
                  Old enough for their own account. The household link and their Pathshala
                  history are kept.
                </div>

                @if (convertError()[child.id]; as message) {
                  <p class="notice error" role="alert">{{ message }}</p>
                }

                @if (converting() === child.id) {
                  <form (ngSubmit)="startConversion(child)">
                    <label [for]="'contact-' + child.id">Their mobile or email</label>
                    <input
                      class="input"
                      [id]="'contact-' + child.id"
                      name="contact"
                      [(ngModel)]="contact"
                      placeholder="aarav@example.com"
                      required
                    />
                    <div class="actions">
                      <button class="btn" type="submit" [disabled]="busy() || !contact.trim()">
                        Send for approval
                      </button>
                      <button class="btn secondary" type="button" (click)="converting.set(null)">
                        Cancel
                      </button>
                    </div>
                  </form>
                } @else {
                  <div class="actions">
                    <button class="btn" type="button" (click)="beginConversion(child)">
                      Register as a main account
                    </button>
                  </div>
                }
              }

              @if (child.parentalConsent; as consent) {
                <p class="small">
                  Consent recorded {{ date(consent.givenAt) }} against notice
                  {{ consent.noticeVersion }}.
                </p>
              }
            </div>
          }

          <!-- Add a child ------------------------------------------------ -->
          @if (household.viewerIsHead) {
            <div class="card">
              <h3>Add a child</h3>

              @if (!addingChild()) {
                <p class="small">Create a child profile linked to this household.</p>
                <div class="actions">
                  <button class="btn secondary" type="button" (click)="beginAddChild()">
                    Add child
                  </button>
                </div>
              } @else if (notice(); as dataNotice) {
                <form (ngSubmit)="addChild(dataNotice)">
                  <label for="child-name">Full name</label>
                  <input
                    class="input"
                    id="child-name"
                    name="childName"
                    [(ngModel)]="childName"
                    maxlength="150"
                    required
                  />

                  <label for="child-dob">Date of birth</label>
                  <input
                    class="input"
                    id="child-dob"
                    name="childDob"
                    type="date"
                    [(ngModel)]="childDob"
                    required
                  />

                  <label for="child-gender">Gender</label>
                  <select class="input" id="child-gender" name="childGender"
                    [(ngModel)]="childGender">
                    @for (option of genders; track option) {
                      <option [value]="option">{{ option }}</option>
                    }
                  </select>

                  <!-- Shown before the tick, never after, and never
                       pre-filled: consent to something nobody has read is not
                       consent (DPDP s.9). -->
                  <div class="notice info">
                    <p class="small">{{ dataNotice.summary }}</p>
                  </div>

                  <label class="consent">
                    <input type="checkbox" name="consent" [(ngModel)]="consentGiven" />
                    {{ dataNotice.attestation }}
                  </label>

                  @if (childError(); as message) {
                    <p class="notice error" role="alert">{{ message }}</p>
                  }

                  <div class="actions">
                    <button
                      class="btn"
                      type="submit"
                      [disabled]="busy() || !consentGiven || !canAddChild()"
                    >
                      Add child
                    </button>
                    <button class="btn secondary" type="button" (click)="cancelAddChild()">
                      Cancel
                    </button>
                  </div>
                </form>
              } @else {
                <p role="status">Loading what you need to agree to…</p>
              }
            </div>
          }
        </div>
      } @else {
        <!-- No household yet ------------------------------------------- -->
        <div class="grid2">
          <div class="card">
            <h3>Start a household</h3>
            <p class="small">
              You become its head, and get a code to give to the rest of your family.
            </p>

            @if (startError(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            <div class="actions">
              <button class="btn" type="button" [disabled]="busy()" (click)="createFamily()">
                Create a household
              </button>
            </div>
          </div>

          <div class="card">
            <h3>Join a household</h3>
            <form (ngSubmit)="join()">
              <label for="family-code">Family code</label>
              <input
                class="input"
                id="family-code"
                name="familyCode"
                [(ngModel)]="familyCode"
                placeholder="The code your family head gave you"
                required
              />

              <label for="relationship">Your relationship to them</label>
              <select class="input" id="relationship" name="relationship"
                [(ngModel)]="relationship">
                @for (option of relationships; track option) {
                  <option [value]="option">{{ option }}</option>
                }
              </select>

              @if (joinError(); as message) {
                <p class="notice error" role="alert">{{ message }}</p>
              }

              @if (joinSent()) {
                <p class="notice info" role="status">
                  Sent. The family head decides; check back here.
                </p>
              }

              <div class="actions">
                <button class="btn" type="submit" [disabled]="busy() || !familyCode.trim()">
                  Request to join
                </button>
              </div>
            </form>
          </div>
        </div>
      }
    </div>
  `,
})
export class FamilyComponent implements OnInit {
  private readonly api = inject(MembersApi);
  private readonly auth = inject(AuthService);

  readonly relationships = Relationships;
  readonly genders = Genders;

  readonly family = signal<Family | null>(null);
  readonly children = signal<readonly Child[]>([]);
  readonly notice = signal<ChildDataNotice | null>(null);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);

  readonly startError = signal<string | null>(null);
  readonly joinError = signal<string | null>(null);
  readonly joinSent = signal(false);
  readonly childError = signal<string | null>(null);

  /** Per-request failures, so one refusal does not blank the queue. */
  readonly actionError = signal<Record<string, string>>({});
  readonly convertError = signal<Record<string, string>>({});

  readonly addingChild = signal(false);
  readonly converting = signal<string | null>(null);

  familyCode = '';
  relationship: Relationship = 'Spouse';
  childName = '';
  childDob = '';
  childGender = 'Unspecified';
  consentGiven = false;
  contact = '';

  private readonly me = computed(() => this.auth.user()?.userId ?? null);

  ngOnInit(): void {
    // The household names people by id and the screen labels the reader as
    // "You", so it waits for /me first.
    this.auth.ensureCurrentUser().subscribe({
      next: () => this.load(),
      error: () => this.load(),
    });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.myFamily().subscribe({
      next: (found) => {
        this.family.set(found);
        this.loading.set(false);
        this.loadChildren();
      },
      error: (failure: unknown) => {
        // 404 is the ordinary case for somebody with no household yet, and the
        // screen has a whole branch for it - it is not an error.
        if (this.isNotFound(failure)) {
          this.family.set(null);
          this.children.set([]);
        } else {
          this.error.set(describeError(failure));
        }

        this.loading.set(false);
      },
    });
  }

  private loadChildren(): void {
    this.api.children().subscribe({
      next: (found) => this.children.set(found),

      // The household is still readable without them, so this does not become
      // a page-level error.
      error: () => this.children.set([]),
    });
  }

  private isNotFound(failure: unknown): boolean {
    return (
      typeof failure === 'object' &&
      failure !== null &&
      (failure as { status?: number }).status === 404
    );
  }

  // ---- Starting or joining one -------------------------------------------

  createFamily(): void {
    this.busy.set(true);
    this.startError.set(null);

    this.api.createFamily().subscribe({
      next: (created) => {
        this.family.set(created);
        this.busy.set(false);
        this.loadChildren();
      },
      error: (failure: unknown) => {
        this.startError.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  join(): void {
    const code = this.familyCode.trim();

    if (code.length === 0) {
      return;
    }

    this.busy.set(true);
    this.joinError.set(null);
    this.joinSent.set(false);

    this.api.requestToJoin(code, this.relationship).subscribe({
      next: () => {
        this.familyCode = '';
        this.joinSent.set(true);
        this.busy.set(false);

        // Not loaded as the household: the request is pending, and showing it
        // as theirs before the head accepts would be a lie.
      },
      error: (failure: unknown) => {
        this.joinError.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  decide(household: Family, request: FamilyMember, accept: boolean): void {
    this.busy.set(true);
    this.clearFrom(this.actionError, request.id);

    this.api.decideJoinRequest(household.id, request.id, accept).subscribe({
      next: (updated) => {
        this.family.set(updated);
        this.busy.set(false);
      },
      error: (failure: unknown) => {
        this.addTo(this.actionError, request.id, describeError(failure));
        this.busy.set(false);
      },
    });
  }

  // ---- Children -----------------------------------------------------------

  /**
   * Fetches the notice, then offers the form.
   *
   * In that order deliberately: a consent tick beside a notice that has not
   * arrived is a tick against nothing.
   */
  beginAddChild(): void {
    this.addingChild.set(true);
    this.childError.set(null);
    this.consentGiven = false;

    if (this.notice() !== null) {
      return;
    }

    this.api.childDataNotice().subscribe({
      next: (found) => this.notice.set(found),
      error: (failure: unknown) => {
        this.childError.set(describeError(failure));
        this.addingChild.set(false);
      },
    });
  }

  cancelAddChild(): void {
    this.addingChild.set(false);
    this.childName = '';
    this.childDob = '';
    this.consentGiven = false;
    this.childError.set(null);
  }

  canAddChild(): boolean {
    return this.childName.trim().length > 0 && this.childDob.length > 0;
  }

  addChild(notice: ChildDataNotice): void {
    if (!this.canAddChild() || !this.consentGiven) {
      return;
    }

    this.busy.set(true);
    this.childError.set(null);

    this.api
      .addChild(this.childName.trim(), this.childDob, this.childGender, notice.version)
      .subscribe({
        next: (created) => {
          this.children.set([...this.children(), created]);
          this.cancelAddChild();
          this.busy.set(false);
        },
        error: (failure: unknown) => {
          this.childError.set(describeError(failure));
          this.busy.set(false);
        },
      });
  }

  beginConversion(child: Child): void {
    this.contact = '';
    this.clearFrom(this.convertError, child.id);
    this.converting.set(child.id);
  }

  startConversion(child: Child): void {
    const contact = this.contact.trim();

    if (contact.length === 0) {
      return;
    }

    this.busy.set(true);
    this.clearFrom(this.convertError, child.id);

    this.api.startConversion(child.id, contact).subscribe({
      next: () => {
        this.converting.set(null);
        this.contact = '';
        this.busy.set(false);
        this.loadChildren();
      },
      error: (failure: unknown) => {
        this.addTo(this.convertError, child.id, describeError(failure));
        this.busy.set(false);
      },
    });
  }

  // ---- Rendering ----------------------------------------------------------

  activeMembers(household: Family): readonly FamilyMember[] {
    return household.members.filter((person) => person.status === 'Active');
  }

  pendingRequests(household: Family): readonly FamilyMember[] {
    return household.members.filter((person) => person.status === 'PendingJoinRequest');
  }

  /** The household carries names, so this only has to mark the reader. */
  nameFor(person: FamilyMember, household: Family): string {
    if (person.memberProfileId === this.me()) {
      return `${person.fullName} (you)`;
    }

    return person.memberProfileId === household.familyHeadMemberId
      ? `${person.fullName} (head)`
      : person.fullName;
  }

  date(iso: string): string {
    const date = new Date(iso);

    return Number.isNaN(date.getTime())
      ? iso
      : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  }

  private addTo(
    target: { (): Record<string, string>; set: (value: Record<string, string>) => void },
    id: string,
    message: string,
  ): void {
    target.set({ ...target(), [id]: message });
  }

  private clearFrom(
    target: { (): Record<string, string>; set: (value: Record<string, string>) => void },
    id: string,
  ): void {
    const { [id]: _removed, ...rest } = target();

    target.set(rest);
  }
}
