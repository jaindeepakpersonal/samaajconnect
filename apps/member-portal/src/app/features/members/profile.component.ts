import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthedImageDirective, describeError } from '@samaajconnect/shared';
import { MasterDataApi } from './master-data.api';
import { MemberCommunityComponent } from './member-community.component';
import { MembersApi } from './members.api';
import { ProfileHeaderComponent } from './profile-header.component';
import { Address, City, Gautra, Gender, LookupItem, Locality, MyProfile, PrivacyLevel } from './members.models';

/**
 * My Profile, from the member-portal wireframe's `#profile` screen.
 *
 * The platform has been telling every new member to "complete your profile to
 * appear in the member directory" since registration first raised a welcome
 * notification, and there was nowhere to do it. `PATCH /v1/members/{id}` and
 * even `MembersApi.updateMe` existed; no screen called either.
 *
 * **Five cards now, not one, as of 2026-09-11: Basic, Professional,
 * Educational, Address and Photo.** The screen used to mix a member's basic
 * identity fields, their profession and their address into one long card.
 * Splitting it did not need a bigger form - it needed the fields grouped by
 * what they are, which is also what let Gautra (Basic) and Qualification
 * (Educational) each land somewhere that reads as their own kind of fact
 * rather than one more row bolted onto whichever card was open.
 *
 * **Gautra, Qualification and, since 2026-09-12, Profession are all
 * master-data-service picks, not free text.** `MasterDataApi` (new when
 * Gautra/Qualification landed - member-portal had never called this service
 * before) loads this Samaaj's own Gautra list and the platform's Qualification
 * and ProfessionType lookup lists once on init, the same "small closed list,
 * fetched once" shape `AuthedImageDirective`'s own photo handling and the
 * Pathshala child picker already use elsewhere in this app. Profession was the
 * one field the original five-card split deliberately left as free text -
 * "just relocate it" was the explicit choice at the time - and became a
 * dropdown once master-data-service's existing `ProfessionType` category (one
 * of the platform's original four flat lookup lists, present since before
 * Qualification was ever added) was the obvious place to source it from.
 * `MemberProfile.Profession: string?` became `ProfessionId: Guid?` on the
 * backend; the migration drops the free-text column rather than attempting to
 * resolve old values into lookup ids, the same "cannot be recovered from old
 * free text" call already made for `PermanentAddress.LocalityId`.
 *
 * **Address became Permanent and Temporary, each a line plus a City and a
 * Locality picked from master-data-service - not two lines of free text.**
 * Locality used to be a plain text field; it is a structured pick now, scoped
 * to whichever City is chosen, so the two selects cascade: choosing a City
 * loads that City's Localities and clears whatever Locality was previously
 * selected, because a Locality from the old City would be nonsense once the
 * City changes. "Same as permanent" is a one-time copy, not a link the two
 * addresses stay bound by afterwards - checking it fills the Temporary
 * fields from the Permanent ones and disables them; unchecking hands them
 * back for independent editing from wherever the copy left them.
 *
 * **Per-field privacy and "listed in directory" moved to Privacy settings
 * (`/privacy`).** This screen used to carry them itself, on the reasoning
 * that every field a member fills in here is visible to their Samaaj unless
 * they say otherwise, so the levels belonged beside the fields rather than
 * behind a separate settings page. That stopped being where a member would
 * look for them once the top bar's user menu started calling the
 * destination "Privacy settings" - a member following that link and finding
 * nothing about who sees what would have been the mismatch, not the page it
 * used to be on. `form.privacy` and `form.isListedInDirectory` are still
 * here and still sent on every save, unedited - `save()`'s request still
 * replaces the whole profile, so both have to travel with it - but nothing
 * on this screen offers to change them any more, and the note above the
 * Save button says where to.
 *
 * **The photo is a real upload now, and it is saved on its own.** It used to be
 * a link field, because the platform hosted no images and the honest control was
 * the one matching what the API took — with a note saying what a link cost:
 * every member who opened the directory fetched it from whatever host it named.
 * The platform stores the bytes itself now, so the control is a file input and
 * the note says what that buys instead.
 *
 * Uploading is its own request rather than part of Save, because a file and a
 * form field are different things and they were only ever one control while the
 * photo was text somebody typed. It also means choosing a picture takes effect
 * immediately, which is what people expect of a profile photo, and a failed
 * upload cannot lose an address somebody was halfway through editing.
 *
 * **The five cards became five tabs on 2026-09-12.** Stacked, a member
 * scrolled past four cards' worth of fields to reach Photo whether or not
 * they had come here to change it. One tab is shown at a time now, the same
 * "one type at a time" shape admin-portal's Master Data screen was redesigned
 * around the same day. All five still submit through the one `<form>` and one
 * Save button below the tabs - switching tabs never touches `form`, so a
 * half-edited Address survives a detour to Educational and back. A failed
 * validation jumps back to Basic, because that is the only tab the one
 * client-side check (a required full name) can ever fail on, and an error a
 * member cannot see is not a validation message.
 */
type ProfileTab = 'basic' | 'professional' | 'educational' | 'address' | 'photo';

@Component({
  selector: 'app-profile',
  imports: [AuthedImageDirective, FormsModule, RouterLink, ProfileHeaderComponent, MemberCommunityComponent],
  styleUrl: './members.css',
  template: `
    <div class="members-page">
      @if (error(); as message) {
        <p class="notice error" role="alert">{{ message }}</p>
      }

      @if (saved()) {
        <p class="notice info" role="status">Your profile has been saved.</p>
      }

      @if (loading()) {
        <h1 class="page-title">My Profile</h1>
        <p class="notice info" role="status">Loading your profile…</p>
      } @else if (profile(); as me) {
        <app-profile-header
          [name]="me.fullName"
          [gautra]="nameOf(gautras(), me.gautraId)"
          [photoUrl]="me.photoUrl"
          [details]="headline(me)"
        >
          @if (!editing()) {
            <button actions class="btn" type="button" (click)="startEditing()">Edit profile</button>
          }
        </app-profile-header>

        @if (!editing()) {
          <!-- View mode: what is on file, read-only, the way a member's page
               reads to others - except that nothing here is hidden from you. -->
          <div class="grid2">
            <div class="card">
              <h2>About</h2>
              <dl class="profile">
                <dt>Mobile</dt>
                <dd>{{ me.mobile ?? notSet }}</dd>
                <dt>Email</dt>
                <dd>{{ me.email ?? notSet }}</dd>
                <dt>Date of birth</dt>
                <dd>{{ me.dateOfBirth ? day(me.dateOfBirth) : notSet }}</dd>
                <dt>Gender</dt>
                <dd>{{ genderLabel(me.gender) }}</dd>
                <dt>Gautra</dt>
                <dd>{{ nameOf(gautras(), me.gautraId) ?? notSet }}</dd>
                <dt>Profession</dt>
                <dd>{{ labelOf(professions(), me.professionId) ?? notSet }}</dd>
                <dt>Qualification</dt>
                <dd>{{ labelOf(qualifications(), me.qualificationId) ?? notSet }}</dd>
              </dl>
              <p class="small">
                Who in your Samaaj sees each of these is set on
                <a routerLink="/privacy">Privacy settings</a>.
              </p>
            </div>

            <div class="card">
              <h2>Address</h2>
              <dl class="profile">
                <dt>Permanent</dt>
                <dd>{{ addressText(me.permanentAddress, permanentLocalities()) }}</dd>
                <dt>Temporary</dt>
                <dd>{{ me.temporaryAddress ? addressText(me.temporaryAddress, temporaryLocalities()) : notSet }}</dd>
              </dl>
            </div>
          </div>

        } @else {
        <div class="tabs" role="tablist" aria-label="Profile section">
          @for (tab of tabs; track tab.key) {
            <button
              type="button"
              role="tab"
              [class.on]="activeTab() === tab.key"
              [attr.aria-selected]="activeTab() === tab.key"
              (click)="activeTab.set(tab.key)"
            >
              {{ tab.label }}
            </button>
          }
        </div>

        <form (ngSubmit)="save()">
          <!-- Basic information ------------------------------------------- -->
          @if (activeTab() === 'basic') {
          <div class="card">
            <h2>Basic information</h2>

            <div class="form-grid">
              <div class="field span-all">
                <label for="full-name">Full name</label>
                <input
                  class="input"
                  id="full-name"
                  name="fullName"
                  [(ngModel)]="form.fullName"
                  maxlength="200"
                  required
                  [attr.aria-invalid]="attempted() && !form.fullName.trim() ? 'true' : null"
                />
                @if (attempted() && !form.fullName.trim()) {
                  <p class="field-error">Your Samaaj needs a name to list you by.</p>
                }
              </div>

              <div class="field">
                <label for="mobile">Mobile</label>
                <input
                  class="input"
                  id="mobile"
                  name="mobile"
                  [(ngModel)]="form.mobile"
                  maxlength="20"
                  placeholder="+91 98765 43210"
                />
              </div>

              <div class="field">
                <label for="email">Email</label>
                <input
                  class="input"
                  id="email"
                  name="email"
                  type="email"
                  [(ngModel)]="form.email"
                  maxlength="320"
                />
              </div>

              <div class="field">
                <label for="dob">Date of birth</label>
                <input
                  class="input"
                  id="dob"
                  name="dateOfBirth"
                  type="date"
                  [(ngModel)]="form.dateOfBirth"
                  [max]="today"
                />
              </div>

              <div class="field">
                <label for="gender">Gender</label>
                <select class="input" id="gender" name="gender" [(ngModel)]="form.gender">
                  @for (option of genders; track option.value) {
                    <option [value]="option.value">{{ option.label }}</option>
                  }
                </select>
              </div>

              <div class="field">
                <label for="gautra">Gautra</label>
                <select class="input" id="gautra" name="gautra" [(ngModel)]="form.gautraId">
                  <option value="">Not set</option>
                  @for (option of gautras(); track option.id) {
                    <option [value]="option.id">{{ option.name }}</option>
                  }
                </select>
              </div>
            </div>
          </div>
          }

          <!-- Professional ---------------------------------------------------- -->
          @if (activeTab() === 'professional') {
          <div class="card">
            <h2>Professional</h2>

            <label for="profession">Profession</label>
            <select class="input" id="profession" name="profession" [(ngModel)]="form.professionId">
              <option value="">Not set</option>
              @for (option of professions(); track option.id) {
                <option [value]="option.id">{{ option.label }}</option>
              }
            </select>
          </div>
          }

          <!-- Educational ------------------------------------------------------ -->
          @if (activeTab() === 'educational') {
          <div class="card">
            <h2>Educational</h2>

            <label for="qualification">Qualification</label>
            <select
              class="input"
              id="qualification"
              name="qualification"
              [(ngModel)]="form.qualificationId"
            >
              <option value="">Not set</option>
              @for (option of qualifications(); track option.id) {
                <option [value]="option.id">{{ option.label }}</option>
              }
            </select>
          </div>
          }

          <!-- Address ------------------------------------------------------ -->
          @if (activeTab() === 'address') {
          <div class="card">
            <h2>Address</h2>

            <h3>Permanent</h3>

            <div class="form-grid">
              <div class="field span-all">
                <label for="permanent-line">Address</label>
                <textarea
                  class="input"
                  id="permanent-line"
                  name="permanentLine"
                  rows="3"
                  [(ngModel)]="form.permanentAddress.line"
                  maxlength="500"
                ></textarea>
              </div>

              <div class="field">
                <label for="permanent-city">City/Village</label>
                <select
                  class="input"
                  id="permanent-city"
                  name="permanentCity"
                  [ngModel]="form.permanentAddress.cityId"
                  (ngModelChange)="onCityChange('permanent', $event)"
                >
                  <option value="">Choose a city/village</option>
                  @for (option of cities(); track option.id) {
                    <option [value]="option.id">{{ option.name }}</option>
                  }
                </select>
              </div>

              <div class="field">
                <label for="permanent-locality">Locality</label>
                <select
                  class="input"
                  id="permanent-locality"
                  name="permanentLocality"
                  [(ngModel)]="form.permanentAddress.localityId"
                  [disabled]="!form.permanentAddress.cityId"
                >
                  <option value="">Choose a locality</option>
                  @for (option of permanentLocalities(); track option.id) {
                    <option [value]="option.id">{{ option.name }}</option>
                  }
                </select>
              </div>
            </div>

            <p class="small">
              Members can filter the directory by locality, so this one is always visible.
            </p>

            <h3>Temporary</h3>

            <div class="privacy-row listing-row">
              <label for="same-as-permanent">Same as permanent</label>
              <input
                id="same-as-permanent"
                type="checkbox"
                name="sameAsPermanent"
                [ngModel]="sameAsPermanent()"
                (ngModelChange)="toggleSameAsPermanent($event)"
              />
            </div>

            <div class="form-grid">
              <div class="field span-all">
                <label for="temporary-line">Address</label>
                <textarea
                  class="input"
                  id="temporary-line"
                  name="temporaryLine"
                  rows="3"
                  [(ngModel)]="form.temporaryAddress.line"
                  [disabled]="sameAsPermanent()"
                  maxlength="500"
                ></textarea>
              </div>

              <div class="field">
                <label for="temporary-city">City/Village</label>
                <select
                  class="input"
                  id="temporary-city"
                  name="temporaryCity"
                  [ngModel]="form.temporaryAddress.cityId"
                  (ngModelChange)="onCityChange('temporary', $event)"
                  [disabled]="sameAsPermanent()"
                >
                  <option value="">Choose a city/village</option>
                  @for (option of cities(); track option.id) {
                    <option [value]="option.id">{{ option.name }}</option>
                  }
                </select>
              </div>

              <div class="field">
                <label for="temporary-locality">Locality</label>
                <select
                  class="input"
                  id="temporary-locality"
                  name="temporaryLocality"
                  [(ngModel)]="form.temporaryAddress.localityId"
                  [disabled]="sameAsPermanent() || !form.temporaryAddress.cityId"
                >
                  <option value="">Choose a locality</option>
                  @for (option of temporaryLocalities(); track option.id) {
                    <option [value]="option.id">{{ option.name }}</option>
                  }
                </select>
              </div>
            </div>
          </div>
          }

          <!-- Photo ---------------------------------------------------------- -->
          @if (activeTab() === 'photo') {
          <div class="card">
            <h2>Photo</h2>

            @if (photoPath(); as path) {
              <img class="profile-photo" [scAuthedSrc]="path" alt="Your profile photo" />
            } @else {
              <p class="small">You have not added a photo.</p>
            }

            <label for="photo">Choose a photo</label>
            <input
              class="input"
              id="photo"
              name="photo"
              type="file"
              accept="image/jpeg,image/png,image/webp"
              [disabled]="uploading()"
              (change)="choosePhoto($event)"
            />
            <p class="small">
              JPEG, PNG or WebP, up to 2 MB. The platform stores it and serves it to your
              Samaaj itself — nobody else's website is asked for it, and nobody outside your
              Samaaj can fetch it.
            </p>

            @if (photoError(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            @if (photoNotice(); as message) {
              <p class="notice info" role="status">{{ message }}</p>
            }

            @if (photoPath()) {
              <div class="actions">
                <button
                  class="btn secondary"
                  type="button"
                  [disabled]="uploading()"
                  (click)="removePhoto()"
                >
                  Remove photo
                </button>
              </div>
            }
          </div>
          }

          <p class="small">
            Changes to your profile are recorded in your account activity, including which
            fields changed. Who can see each field is set on
            <a routerLink="/privacy">Privacy settings</a>.
          </p>

          <div class="actions">
            <button class="btn" type="submit" [disabled]="saving()">
              {{ saving() ? 'Saving…' : 'Save changes' }}
            </button>
            <button class="btn secondary" type="button" [disabled]="saving()" (click)="cancelEditing()">
              Cancel
            </button>
          </div>
        </form>
        }

        <!-- Outside the edit switch, so it is created once and stays put while
             the member edits rather than re-reading on every toggle. -->
        <div class="grid2 community-grid">
          <app-member-community [memberId]="me.id" />
        </div>
      }
    </div>
  `,
})
export class ProfileComponent implements OnInit {
  private readonly api = inject(MembersApi);
  private readonly master = inject(MasterDataApi);

  readonly tabs: { key: ProfileTab; label: string }[] = [
    { key: 'basic', label: 'Basic' },
    { key: 'professional', label: 'Professional' },
    { key: 'educational', label: 'Educational' },
    { key: 'address', label: 'Address' },
    { key: 'photo', label: 'Photo' },
  ];

  readonly activeTab = signal<ProfileTab>('basic');

  /** View mode until the member asks to edit - a profile is read far more often than changed. */
  readonly editing = signal(false);

  /** What an empty field reads as on your own profile - unlike someone else's, nothing is hidden from you. */
  readonly notSet = 'Not set';

  readonly profile = signal<MyProfile | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly saved = signal(false);
  readonly attempted = signal(false);
  readonly error = signal<string | null>(null);

  readonly uploading = signal(false);
  readonly photoError = signal<string | null>(null);
  readonly photoNotice = signal<string | null>(null);

  readonly gautras = signal<readonly Gautra[]>([]);
  readonly professions = signal<readonly LookupItem[]>([]);
  readonly qualifications = signal<readonly LookupItem[]>([]);
  readonly cities = signal<readonly City[]>([]);
  readonly permanentLocalities = signal<readonly Locality[]>([]);
  readonly temporaryLocalities = signal<readonly Locality[]>([]);

  /** True while the Temporary fields are a disabled copy of Permanent. */
  readonly sameAsPermanent = signal(false);

  /**
   * Where the member's own photo is, or null.
   *
   * Read off the profile rather than kept separately, so that after an upload
   * the screen shows what the server says it holds rather than what the browser
   * thinks it just sent.
   */
  readonly photoPath = computed(() => this.profile()?.photoUrl ?? null);

  /** Stops a member dating their birth in the future before the server has to. */
  readonly today = new Date().toISOString().slice(0, 10);

  readonly genders: { value: Gender; label: string }[] = [
    { value: 'Unspecified', label: 'Prefer not to say' },
    { value: 'Female', label: 'Female' },
    { value: 'Male', label: 'Male' },
    { value: 'Other', label: 'Other' },
  ];

  /**
   * The editable copy. Bound with `ngModel` rather than to the signal, so a
   * half-typed form is never mistaken for what the server holds — and `''` is
   * seeded for every optional field, because an `undefined` bound to a control
   * renders as an empty one that then posts `undefined`.
   */
  form = {
    fullName: '',
    mobile: '',
    email: '',
    dateOfBirth: '',
    gender: 'Unspecified' as Gender,
    gautraId: '',
    professionId: '',
    qualificationId: '',
    permanentAddress: { line: '', cityId: '', localityId: '' },
    temporaryAddress: { line: '', cityId: '', localityId: '' },
    isListedInDirectory: true,
    // Overwritten by `fill()` before the form is ever rendered — the template
    // only draws once `profile()` is set. These match `FieldPrivacy.Default`
    // anyway, because a placeholder that quietly disagreed with the service
    // would be a second copy of the defaults, and the wrong one.
    privacy: {
      mobile: 'SamaajOnly' as PrivacyLevel,
      email: 'Private' as PrivacyLevel,
      address: 'Private' as PrivacyLevel,
      profession: 'SamaajOnly' as PrivacyLevel,
      dateOfBirth: 'SamaajOnly' as PrivacyLevel,
      qualification: 'SamaajOnly' as PrivacyLevel,
    },
  };

  ngOnInit(): void {
    this.load();

    this.master.listGautras().subscribe({ next: (found) => this.gautras.set(found), error: () => {} });
    this.master.listProfessions().subscribe({
      next: (found) => this.professions.set(found),
      error: () => {},
    });
    this.master.listQualifications().subscribe({
      next: (found) => this.qualifications.set(found),
      error: () => {},
    });
    this.master.listCities().subscribe({ next: (found) => this.cities.set(found), error: () => {} });
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.me().subscribe({
      next: (me) => {
        this.profile.set(me);
        this.fill(me);
        this.loading.set(false);
      },
      error: (failure) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }

  private fill(me: MyProfile): void {
    this.form = {
      fullName: me.fullName,
      mobile: me.mobile ?? '',
      email: me.email ?? '',
      // The service sends a date, the control wants yyyy-MM-dd, and they
      // already agree — but a value with a time on it would silently render as
      // an empty date input rather than as an error.
      dateOfBirth: me.dateOfBirth ? me.dateOfBirth.slice(0, 10) : '',
      gender: me.gender,
      gautraId: me.gautraId ?? '',
      professionId: me.professionId ?? '',
      qualificationId: me.qualificationId ?? '',
      permanentAddress: toFormAddress(me.permanentAddress),
      temporaryAddress: toFormAddress(me.temporaryAddress),
      isListedInDirectory: me.isListedInDirectory,
      privacy: { ...me.privacy },
    };

    this.sameAsPermanent.set(false);

    // A City already chosen needs its Localities loaded, or the select would
    // show the id with no matching option - the same trap an uninitialised
    // <select> falls into, one step later.
    if (this.form.permanentAddress.cityId) {
      this.loadLocalities('permanent', this.form.permanentAddress.cityId);
    }

    if (this.form.temporaryAddress.cityId) {
      this.loadLocalities('temporary', this.form.temporaryAddress.cityId);
    }
  }

  // ---- Address ------------------------------------------------------------

  onCityChange(which: 'permanent' | 'temporary', cityId: string): void {
    const address = which === 'permanent' ? this.form.permanentAddress : this.form.temporaryAddress;

    address.cityId = cityId;
    // A Locality from the old City is nonsense once the City changes.
    address.localityId = '';

    if (which === 'permanent') {
      this.permanentLocalities.set([]);
    } else {
      this.temporaryLocalities.set([]);
    }

    if (cityId) {
      this.loadLocalities(which, cityId);
    }
  }

  private loadLocalities(which: 'permanent' | 'temporary', cityId: string): void {
    this.master.listLocalities(cityId).subscribe({
      next: (found) => {
        if (which === 'permanent') {
          this.permanentLocalities.set(found);
        } else {
          this.temporaryLocalities.set(found);
        }
      },
      error: () => {},
    });
  }

  /**
   * A one-time copy, not a link the two addresses stay bound by. Checking
   * fills Temporary from Permanent and disables it; unchecking hands the
   * fields back for independent editing from wherever the copy left them.
   */
  toggleSameAsPermanent(checked: boolean): void {
    this.sameAsPermanent.set(checked);

    if (checked) {
      this.form.temporaryAddress = { ...this.form.permanentAddress };
      this.temporaryLocalities.set(this.permanentLocalities());
    }
  }

  startEditing(): void {
    this.saved.set(false);
    this.error.set(null);
    this.activeTab.set('basic');
    this.editing.set(true);
  }

  /** Back to view mode, putting back what is on file - nothing typed survives a cancel. */
  cancelEditing(): void {
    const me = this.profile();

    if (me !== null) {
      this.fill(me);
    }

    this.attempted.set(false);
    this.error.set(null);
    this.editing.set(false);
  }

  /** The line under the name: profession and locality, when set. */
  headline(me: MyProfile): string[] {
    return [
      this.labelOf(this.professions(), me.professionId),
      this.nameOf(this.permanentLocalities(), me.permanentAddress.localityId),
    ].filter((part): part is string => part !== null);
  }

  nameOf(list: readonly { id: string; name: string }[], id: string | null): string | null {
    return id === null ? null : (list.find((item) => item.id === id)?.name ?? null);
  }

  labelOf(list: readonly LookupItem[], id: string | null): string | null {
    return id === null ? null : (list.find((item) => item.id === id)?.label ?? null);
  }

  genderLabel(gender: Gender): string {
    return this.genders.find((option) => option.value === gender)?.label ?? gender;
  }

  /** "12 Old Road, Hiran Magri, Udaipur" - whatever parts are set. */
  addressText(address: Address, localities: readonly Locality[]): string {
    const parts = [
      address.line,
      this.nameOf(localities, address.localityId),
      this.nameOf(this.cities(), address.cityId),
    ].filter((part): part is string => typeof part === 'string' && part.trim().length > 0);

    return parts.length === 0 ? this.notSet : parts.join(', ');
  }

  day(iso: string): string {
    const [year = 1970, month = 1, date = 1] = iso.slice(0, 10).split('-').map((part) => Number(part));

    return new Date(year, month - 1, date).toLocaleDateString(undefined, {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    });
  }

  save(): void {
    this.attempted.set(true);
    this.saved.set(false);
    this.error.set(null);

    const current = this.profile();

    if (current === null || !this.form.fullName.trim()) {
      // The only client-side validation is a required full name, which only
      // ever renders on the Basic tab - jumping there is what makes the error
      // this sets visible rather than silent on whichever tab was open.
      this.activeTab.set('basic');

      return;
    }

    this.saving.set(true);

    const permanentAddress = toWireAddress(this.form.permanentAddress);
    // "No temporary address" is a real, distinct state from one with nothing
    // filled in - sent as null, not as an object every field of which is
    // null, so the two never come apart on the wire.
    const temporaryAddress = isBlankAddress(this.form.temporaryAddress)
      ? null
      : toWireAddress(this.form.temporaryAddress);

    this.api
      .updateMe({
        ...current,
        fullName: this.form.fullName.trim(),
        // Empty means "not set", and the service stores null rather than an
        // empty string — sending '' would make "cleared" and "blank" two
        // different states in one column.
        mobile: blankToNull(this.form.mobile),
        email: blankToNull(this.form.email),
        dateOfBirth: blankToNull(this.form.dateOfBirth),
        gender: this.form.gender,
        gautraId: blankToNull(this.form.gautraId),
        professionId: blankToNull(this.form.professionId),
        qualificationId: blankToNull(this.form.qualificationId),
        permanentAddress,
        temporaryAddress,
        isListedInDirectory: this.form.isListedInDirectory,
        privacy: { ...this.form.privacy },
      })
      .subscribe({
        next: (updated) => {
          // Refilled from the response, not from what was typed. The service
          // trims, lowercases the email and may refuse a value silently; a
          // screen showing its own copy would be the one that is wrong.
          this.profile.set(updated);
          this.fill(updated);
          this.attempted.set(false);
          this.saving.set(false);
          this.saved.set(true);
          this.editing.set(false);
        },
        error: (failure) => {
          this.error.set(describeError(failure));
          this.saving.set(false);
        },
      });
  }

  // ---- The photo ----------------------------------------------------------

  /**
   * Uploads the chosen file straight away.
   *
   * The size is checked here as well as by the service, and that is not a
   * duplicated rule for its own sake: sending two megabytes over a phone
   * connection to be told it was too big is a bad way to find out. The service
   * is still the one that decides — this only saves a round trip it was always
   * going to refuse.
   *
   * The file input is cleared afterwards so that choosing the same file twice
   * fires `change` again. Without it, a member whose first attempt failed could
   * not retry with the same picture.
   */
  choosePhoto(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;

    input.value = '';

    if (file === null) {
      return;
    }

    this.photoError.set(null);
    this.photoNotice.set(null);

    if (file.size > MaxPhotoBytes) {
      this.photoError.set('That photo is larger than 2 MB. Choose a smaller one.');
      return;
    }

    const me = this.profile();

    if (me === null) {
      return;
    }

    this.uploading.set(true);

    this.api.uploadMyPhoto(me.id, file).subscribe({
      next: () => {
        this.uploading.set(false);
        this.photoNotice.set('Your photo has been updated.');
        // Re-read rather than assume. The path is derived by the server, and a
        // screen that invented it would be guessing at a contract it does not
        // own.
        this.reload();
      },
      error: (failure) => {
        this.uploading.set(false);
        this.photoError.set(describeError(failure));
      },
    });
  }

  removePhoto(): void {
    const me = this.profile();

    if (me === null) {
      return;
    }

    this.photoError.set(null);
    this.photoNotice.set(null);
    this.uploading.set(true);

    this.api.removeMyPhoto(me.id).subscribe({
      next: () => {
        this.uploading.set(false);
        this.photoNotice.set('Your photo has been removed.');
        this.reload();
      },
      error: (failure) => {
        this.uploading.set(false);
        this.photoError.set(describeError(failure));
      },
    });
  }

  private reload(): void {
    this.api.me().subscribe({
      next: (updated) => this.profile.set(updated),
      error: (failure) => this.photoError.set(describeError(failure)),
    });
  }
}

/** 2 MB, matching `ImageContent.MaxBytes` in member-family-service. */
const MaxPhotoBytes = 2 * 1024 * 1024;

function blankToNull(value: string): string | null {
  const trimmed = value.trim();

  return trimmed.length === 0 ? null : trimmed;
}

interface FormAddress {
  line: string;
  cityId: string;
  localityId: string;
}

function toFormAddress(address: Address | null): FormAddress {
  return {
    line: address?.line ?? '',
    cityId: address?.cityId ?? '',
    localityId: address?.localityId ?? '',
  };
}

function toWireAddress(address: FormAddress): Address {
  return {
    line: blankToNull(address.line),
    cityId: blankToNull(address.cityId),
    localityId: blankToNull(address.localityId),
  };
}

function isBlankAddress(address: FormAddress): boolean {
  return address.line.trim().length === 0 && !address.cityId && !address.localityId;
}
