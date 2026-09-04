import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthedImageDirective, AuthService, describeError } from '@samaajconnect/shared';
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
  imports: [AuthedImageDirective, FormsModule, RouterLink],
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

      <!-- Page level, not inside the waiting card, and that is the point.
           Withdrawing re-reads so the screen stops claiming to be waiting - but
           the one refusal worth showing is "the head accepted while you were
           deciding", and the re-read is exactly what takes the waiting card
           away. A message inside it would be removed by the same reload that
           made it true. -->
      @if (withdrawError(); as message) {
        <p class="notice info" role="status">{{ message }}</p>
      }

      @if (loading()) {
        <p role="status">Loading your household…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (awaitingDecision(); as household) {
        <!-- Asked, not yet decided ------------------------------------------
             This state had no screen. GET /families/mine returns the household
             to somebody whose request is only pending, so the page rendered it
             as theirs - and there was no way to take the request back, while a
             pending request blocks joining anywhere else or creating one of
             your own. A head who never answered left that member stuck with no
             way out that did not run through somebody else. -->
        <div class="card">
          <h2>Waiting to join a household</h2>

          <p>
            You have asked to join {{ headName(household) }}. They decide, and until
            they do you cannot join another household or create your own.
          </p>

          <div class="actions">
            <button
              class="btn secondary"
              type="button"
              [disabled]="busy()"
              (click)="withdrawRequest()"
            >
              Withdraw my request
            </button>
          </div>

          <p class="small">
            Withdrawing frees you to ask a different household, or to start one.
            It tells nobody, and you can ask this household again later.
          </p>
        </div>
      } @else if (family(); as household) {
        <div class="grid2">
          <!-- The household ---------------------------------------------- -->
          <div class="card">
            <h2>Your household</h2>

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
              <h2>Invite someone</h2>

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

              <h2>Requests to join</h2>

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
              <h2>Your household</h2>
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
              <h2>{{ child.fullName }}</h2>

              <!-- The photo the platform hosts. This is the field DPDP s.9(3)
                   was about: it used to be a link, so every viewer of a child's
                   record told a third-party host that a child's picture had
                   just been looked at. -->
              @if (child.photoUrl; as path) {
                <img class="profile-photo" [scAuthedSrc]="path" [alt]="child.fullName" />
              }

              <p>{{ date(child.dateOfBirth) }} • Age {{ child.age }}</p>

              <label [for]="'child-photo-' + child.id">
                {{ child.photoUrl ? 'Replace photo' : 'Add a photo' }}
              </label>
              <input
                class="input"
                [id]="'child-photo-' + child.id"
                type="file"
                accept="image/jpeg,image/png,image/webp"
                [disabled]="photoBusy() === child.id"
                (change)="chooseChildPhoto(child, $event)"
              />
              <p class="small">
                JPEG, PNG or WebP, up to 2 MB. Kept by the platform and shown only to your
                household — no other website is asked for it.
              </p>

              @if (photoError()[child.id]; as message) {
                <p class="notice error" role="alert">{{ message }}</p>
              }

              @if (child.photoUrl) {
                <div class="actions">
                  <button
                    class="btn secondary"
                    type="button"
                    [disabled]="photoBusy() === child.id"
                    (click)="removeChildPhoto(child)"
                  >
                    Remove photo
                  </button>
                </div>
              }

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
              <h2>Add a child</h2>

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
            <h2>Start a household</h2>
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
            <h2>Join a household</h2>
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

  /** Which child's photo is being uploaded or removed, if any. */
  readonly photoBusy = signal<string | null>(null);
  readonly photoError = signal<Record<string, string>>({});

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

  readonly withdrawError = signal<string | null>(null);

  /**
   * The household this member has asked to join and nobody has decided, or
   * null.
   *
   * `/families/mine` answers with the household for somebody whose request is
   * only pending — deliberately, since a pending request counts as belonging
   * to one — so without this the screen drew it as theirs. Reading the viewer's
   * own row is the only way to tell the two apart.
   */
  readonly awaitingDecision = computed<Family | null>(() => {
    const household = this.family();
    const me = this.me();

    if (household === null || me === null) {
      return null;
    }

    const mine = household.members.find((person) => person.memberProfileId === me);

    return mine?.status === 'PendingJoinRequest' ? household : null;
  });

  /** Who is being asked, so the screen names them rather than an id. */
  headName(household: Family): string {
    const head = household.members.find(
      (person) => person.memberProfileId === household.familyHeadMemberId,
    );

    return head?.fullName ?? 'this household';
  }

  /**
   * Takes the request back.
   *
   * A refusal here is the one case worth showing rather than swallowing: the
   * head accepted while this screen was open, so the member is in the household
   * now and re-reading shows them that.
   */
  withdrawRequest(): void {
    this.withdrawError.set(null);
    this.busy.set(true);

    this.api.withdrawJoinRequest().subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.withdrawError.set(describeError(failure));
        this.load();
      },
    });
  }

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

  // ---- A child's photo ----------------------------------------------------

  /**
   * Uploads the chosen picture straight away.
   *
   * The size is checked here as well as by the service so that a parent on a
   * phone is not asked to send two megabytes in order to be told it was too
   * many. The service still decides; this only avoids a round trip it was
   * always going to refuse.
   *
   * The input is cleared afterwards, or choosing the same file twice fires no
   * `change` event and a failed attempt could not be retried with the same
   * picture.
   */
  chooseChildPhoto(child: Child, event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;

    input.value = '';
    this.clearFrom(this.photoError, child.id);

    if (file === null) {
      return;
    }

    if (file.size > MaxPhotoBytes) {
      this.addTo(this.photoError, child.id, 'That photo is larger than 2 MB. Choose a smaller one.');
      return;
    }

    this.photoBusy.set(child.id);

    this.api.uploadChildPhoto(child.id, file).subscribe({
      next: () => {
        this.photoBusy.set(null);
        // Re-read rather than assume: the path is the server's to derive.
        this.loadChildren();
      },
      error: (failure: unknown) => {
        this.photoBusy.set(null);
        this.addTo(this.photoError, child.id, describeError(failure));
      },
    });
  }

  removeChildPhoto(child: Child): void {
    this.clearFrom(this.photoError, child.id);
    this.photoBusy.set(child.id);

    this.api.removeChildPhoto(child.id).subscribe({
      next: () => {
        this.photoBusy.set(null);
        this.loadChildren();
      },
      error: (failure: unknown) => {
        this.photoBusy.set(null);
        this.addTo(this.photoError, child.id, describeError(failure));
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

/** 2 MB, matching `ImageContent.MaxBytes` in member-family-service. */
const MaxPhotoBytes = 2 * 1024 * 1024;
