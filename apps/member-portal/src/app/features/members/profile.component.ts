import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthedImageDirective, describeError } from '@samaajconnect/shared';
import { MembersApi } from './members.api';
import { Gender, MyProfile, PrivacyLevel } from './members.models';

/**
 * My Profile, from the member-portal wireframe's `#profile` screen.
 *
 * The platform has been telling every new member to "complete your profile to
 * appear in the member directory" since registration first raised a welcome
 * notification, and there was nowhere to do it. `PATCH /v1/members/{id}` and
 * even `MembersApi.updateMe` existed; no screen called either.
 *
 * **The privacy card is the point of the screen, not decoration.** Every field
 * a member fills in here is visible to their Samaaj unless they say otherwise,
 * so the levels sit beside the fields rather than behind a separate settings
 * page.
 *
 * **"Profile listed in directory" is a real setting now.** The wireframe drew a
 * checkbox for it and per-field privacy could not express it: a member who marks
 * every field Private is still in the directory under their name, because a
 * listing is a name. It hides them from the directory search and from nothing
 * else — the screen says so, because a control that reads as "hide me from the
 * platform" would be promising something it does not do.
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
 */
@Component({
  selector: 'app-profile',
  imports: [AuthedImageDirective, FormsModule],
  styleUrl: './members.css',
  template: `
    <div class="members-page">
      <header class="page-head">
        <div>
          <h1 class="page-title">My Profile</h1>
          <p class="subtitle">
            Your details, and who in your Samaaj can see each one.
          </p>
        </div>
      </header>

      @if (error(); as message) {
        <p class="notice error" role="alert">{{ message }}</p>
      }

      @if (saved()) {
        <p class="notice info" role="status">Your profile has been saved.</p>
      }

      @if (loading()) {
        <p class="notice info" role="status">Loading your profile…</p>
      } @else if (profile(); as me) {
        <form class="grid2" (ngSubmit)="save()">
          <!-- Basic information ------------------------------------------- -->
          <div class="card">
            <h2>Basic information</h2>

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

            <label for="mobile">Mobile</label>
            <input
              class="input"
              id="mobile"
              name="mobile"
              [(ngModel)]="form.mobile"
              maxlength="20"
              placeholder="+91 98765 43210"
            />

            <label for="email">Email</label>
            <input
              class="input"
              id="email"
              name="email"
              type="email"
              [(ngModel)]="form.email"
              maxlength="320"
            />

            <label for="dob">Date of birth</label>
            <input
              class="input"
              id="dob"
              name="dateOfBirth"
              type="date"
              [(ngModel)]="form.dateOfBirth"
              [max]="today"
            />

            <label for="gender">Gender</label>
            <select class="input" id="gender" name="gender" [(ngModel)]="form.gender">
              @for (option of genders; track option.value) {
                <option [value]="option.value">{{ option.label }}</option>
              }
            </select>

            <label for="locality">Locality</label>
            <input
              class="input"
              id="locality"
              name="locality"
              [(ngModel)]="form.locality"
              maxlength="120"
              placeholder="Hiran Magri"
            />
            <p class="small">
              Members can filter the directory by locality, so this one is always visible.
            </p>

            <label for="profession">Profession</label>
            <input
              class="input"
              id="profession"
              name="profession"
              [(ngModel)]="form.profession"
              maxlength="120"
            />

            <label for="address">Address</label>
            <textarea
              class="input"
              id="address"
              name="address"
              rows="3"
              [(ngModel)]="form.address"
              maxlength="500"
            ></textarea>

            <!-- The photo is uploaded on its own, not saved with the rest of
                 the form: it is a file rather than a field, and the two were
                 only ever one control because the photo used to be a link
                 somebody typed. -->
            <h3>Photo</h3>

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

          <!-- Privacy ------------------------------------------------------ -->
          <div class="card">
            <h2>Privacy settings</h2>
            <p class="small">Choose who can see each field in the member directory.</p>

            @for (field of privacyFields; track field.key) {
              <div class="privacy-row">
                <label [attr.for]="'privacy-' + field.key">{{ field.label }}</label>
                <select
                  class="input privacy-select"
                  [id]="'privacy-' + field.key"
                  [name]="'privacy-' + field.key"
                  [(ngModel)]="form.privacy[field.key]"
                >
                  @for (level of levels; track level.value) {
                    <option [value]="level.value">{{ level.label }}</option>
                  }
                </select>
              </div>
            }

            <div class="privacy-row listing-row">
              <label for="listed">Listed in the member directory</label>
              <input
                id="listed"
                name="isListedInDirectory"
                type="checkbox"
                [(ngModel)]="form.isListedInDirectory"
              />
            </div>

            <p class="small">
              Unticking this takes you out of the directory search. It does not hide you
              everywhere: your name still appears where you have taken part — on a post you
              wrote, in your family, and to the president of a group you apply to. Your Samaaj's
              administrators can still find you.
            </p>

            <p class="small">
              Changes to your profile are recorded in your account activity, including which
              fields changed.
            </p>

            <div class="actions">
              <button class="btn" type="submit" [disabled]="saving()">
                {{ saving() ? 'Saving…' : 'Save changes' }}
              </button>
            </div>
          </div>
        </form>
      }
    </div>
  `,
})
export class ProfileComponent implements OnInit {
  private readonly api = inject(MembersApi);

  readonly profile = signal<MyProfile | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly saved = signal(false);
  readonly attempted = signal(false);
  readonly error = signal<string | null>(null);

  readonly uploading = signal(false);
  readonly photoError = signal<string | null>(null);
  readonly photoNotice = signal<string | null>(null);

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
   * The five fields that carry a level, in the order the wireframe lists them
   * with date of birth added — it has a level in the domain and leaving it off
   * the screen would mean a setting a member cannot see or change.
   */
  readonly privacyFields = [
    { key: 'mobile', label: 'Mobile number' },
    { key: 'email', label: 'Email' },
    { key: 'address', label: 'Address' },
    { key: 'profession', label: 'Profession' },
    { key: 'dateOfBirth', label: 'Date of birth' },
  ] as const;

  readonly levels: { value: PrivacyLevel; label: string }[] = [
    { value: 'Private', label: 'Only me' },
    { value: 'SamaajOnly', label: 'My Samaaj' },
    { value: 'Public', label: 'Anyone' },
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
    address: '',
    locality: '',
    profession: '',
    dateOfBirth: '',
    gender: 'Unspecified' as Gender,
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
    },
  };

  ngOnInit(): void {
    this.load();
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
      address: me.address ?? '',
      locality: me.locality ?? '',
      profession: me.profession ?? '',
      // The service sends a date, the control wants yyyy-MM-dd, and they
      // already agree — but a value with a time on it would silently render as
      // an empty date input rather than as an error.
      dateOfBirth: me.dateOfBirth ? me.dateOfBirth.slice(0, 10) : '',
      gender: me.gender,
      isListedInDirectory: me.isListedInDirectory,
      privacy: { ...me.privacy },
    };
  }

  save(): void {
    this.attempted.set(true);
    this.saved.set(false);
    this.error.set(null);

    const current = this.profile();

    if (current === null || !this.form.fullName.trim()) {
      return;
    }

    this.saving.set(true);

    this.api
      .updateMe({
        ...current,
        fullName: this.form.fullName.trim(),
        // Empty means "not set", and the service stores null rather than an
        // empty string — sending '' would make "cleared" and "blank" two
        // different states in one column.
        mobile: blankToNull(this.form.mobile),
        email: blankToNull(this.form.email),
        address: blankToNull(this.form.address),
        locality: blankToNull(this.form.locality),
        profession: blankToNull(this.form.profession),
        dateOfBirth: blankToNull(this.form.dateOfBirth),
        gender: this.form.gender,
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
