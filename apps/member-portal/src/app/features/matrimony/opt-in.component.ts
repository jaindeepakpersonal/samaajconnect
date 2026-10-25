import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthedImageDirective, describeError } from '@samaajconnect/shared';
import { MasterDataApi } from '../members/master-data.api';
import { MembersApi } from '../members/members.api';
import { City, Gautra, Locality, LookupItem } from '../members/members.models';
import { MatrimonyApi } from './matrimony.api';
import {
  BiodataFields,
  Diet,
  DietLabels,
  Diets,
  FamilyTypes,
  Gender,
  MatrimonyConsentNotice,
  MatrimonyEligibility,
  MyBiodata,
} from './matrimony.models';

/**
 * My Biodata, `/matrimony` - the DPDP consent notice and opt-in form, which
 * doubles as the edit form once a member has already opted in.
 *
 * **It opens on what is already there (2026-10-24).** A member with a biodata
 * sees it, read-only, with Edit; the form appears only when they ask for it.
 *
 * **Matrimony is for members who are unmarried or divorced.** The page asks
 * matrimony-service first (`eligibility()`), and a member who has not set their
 * marital status, or whose My Profile says Married, is told so and pointed at
 * My Profile instead of being offered a form the service would refuse. That is
 * a courtesy; the service's own refusal is the rule.
 *
 * The starting values (name, DOB, gender, Gautra, profession, qualification,
 * permanent city/locality) are prefilled from `MembersApi.me()`, the base
 * profile this member already holds - a one-time copy, per
 * `matrimony-service/CLAUDE.md`: editing biodata afterwards never reaches
 * back into the base profile, so changing a profession there does not
 * silently change what a candidate's search result shows.
 */
@Component({
  selector: 'app-matrimony-opt-in',
  imports: [FormsModule, RouterLink, AuthedImageDirective],
  styleUrl: './matrimony.css',
  template: `
    <div class="matrimony-page">
      <header class="page-head">
        <div>
          <h1 class="page-title">Matrimony</h1>
          <p class="subtitle">Prepare your biodata, then search and send proposals within your Samaaj.</p>
        </div>
      </header>

      @if (loading()) {
        <p role="status">Loading…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else {
        @if (eligibility() !== 'Eligible') {
          <div class="notice warn" role="status">
            @if (eligibility() === 'Unknown') {
              Matrimony biodata is for members who are unmarried or divorced. Set your marital status
              on <a routerLink="/profile">My Profile</a> first.
            } @else {
              Matrimony biodata is for members who are unmarried or divorced, and your
              <a routerLink="/profile">My Profile</a> says otherwise.
              @if (myBiodata()) {
                Your biodata is kept, and is not shown to anybody while that is so.
              }
            }
          </div>
        }

        @if (myBiodata(); as mine) {
          <!-- Already opted in: what is there, plus links to Search/Proposals. -->
          <div class="badges">
            <span class="pill" [class]="mine.isActive ? 'ok' : 'warn'">
              {{ mine.isActive ? 'Visible in search' : 'Withdrawn - not visible in search' }}
            </span>
          </div>

          <div class="actions">
            <a class="btn small" routerLink="/matrimony/search">Search candidates</a>
            <a class="btn small secondary" routerLink="/matrimony/proposals">My proposals</a>
          </div>

          @if (mine.biodata.photoUrl; as path) {
            <img class="candidate-photo" [scAuthedSrc]="path" alt="Your photo" />
          }
        } @else {
          <!-- Never opted in: show the consent notice, then the opt-in form. -->
          @if (notice(); as consentNotice) {
            <div class="card">
              <h2>Before you opt in</h2>
              <p class="group-body">{{ consentNotice.summary }}</p>
            </div>
          }
        }

        @if (saveError(); as message) {
          <p class="notice error" role="alert">{{ message }}</p>
        }
        @if (saved()) {
          <p class="notice ok" role="status">Your biodata has been saved.</p>
        }

        @if (myBiodata() && !editing()) {
          @if (myBiodata(); as mine) {
            <div class="card">
              <div class="section-head">
                <h2>Your biodata</h2>
                @if (eligibility() === 'Eligible') {
                  <button class="btn small" type="button" (click)="startEditing()">Edit</button>
                }
              </div>

              <dl class="details">
                <dt>Full name</dt><dd>{{ mine.biodata.fullName }}</dd>
                <dt>Gender</dt><dd>{{ mine.biodata.gender }}</dd>
                <dt>Date of birth</dt><dd>{{ mine.dateOfBirth }} ({{ mine.biodata.age }})</dd>
                <dt>Gautra</dt><dd>{{ nameIn(gautras(), mine.biodata.gautraId) }}</dd>
                <dt>Profession</dt><dd>{{ labelIn(professions(), mine.biodata.professionId) }}</dd>
                <dt>Qualification</dt><dd>{{ labelIn(qualifications(), mine.biodata.qualificationId) }}</dd>
                <dt>City/Village</dt><dd>{{ nameIn(cities(), mine.biodata.cityId) }}</dd>
                <dt>Locality</dt><dd>{{ nameIn(localities(), mine.biodata.localityId) }}</dd>
                <dt>Height</dt><dd>{{ mine.biodata.heightCm ? mine.biodata.heightCm + ' cm' : notSet }}</dd>
                <dt>Marital status</dt><dd>{{ labelIn(maritalStatuses(), mine.biodata.maritalStatusId) }}</dd>
                <dt>Diet</dt><dd>{{ mine.biodata.diet ? dietLabel(mine.biodata.diet) : notSet }}</dd>
                <dt>Family type</dt><dd>{{ mine.biodata.familyType ?? notSet }}</dd>
                <dt>Father's occupation</dt><dd>{{ mine.biodata.fatherOccupation ?? notSet }}</dd>
                <dt>Mother's occupation</dt><dd>{{ mine.biodata.motherOccupation ?? notSet }}</dd>
                <dt>Siblings</dt><dd>{{ mine.biodata.siblingsCount ?? notSet }}</dd>
                <dt>Manglik</dt><dd>{{ mine.biodata.manglik === null ? notSet : mine.biodata.manglik ? 'Yes' : 'No' }}</dd>
                <dt>Preferred age</dt>
                <dd>{{ mine.biodata.preferredAgeMin ?? '…' }} to {{ mine.biodata.preferredAgeMax ?? '…' }}</dd>
                <dt>Horoscope notes</dt><dd class="group-body">{{ mine.biodata.horoscopeNotes ?? notSet }}</dd>
                <dt>About me</dt><dd class="group-body">{{ mine.biodata.aboutMe ?? notSet }}</dd>
                <dt>Partner preference</dt><dd class="group-body">{{ mine.biodata.partnerPreferenceNotes ?? notSet }}</dd>
              </dl>
            </div>
          }
        } @else if (myBiodata() || eligibility() === 'Eligible') {
        <form class="card" #optInForm="ngForm" (ngSubmit)="save()">
          <h2>{{ myBiodata() ? 'Edit your biodata' : 'Your biodata' }}</h2>
          <p class="small">Fields marked <span aria-hidden="true">*</span><span class="sr-only">required</span> are required.</p>

          <div class="form-grid">
            <div class="field">
              <label for="fullName">Full name <span aria-hidden="true">*</span><span class="sr-only">required</span></label>
              <input class="input" id="fullName" name="fullName" [(ngModel)]="form.fullName" required maxlength="200" />
            </div>

            <div class="field">
              <label for="gender">Gender <span aria-hidden="true">*</span><span class="sr-only">required</span></label>
              <select class="input" id="gender" name="gender" [(ngModel)]="form.gender" required>
                @for (option of genders; track option) {
                  <option [value]="option">{{ option }}</option>
                }
              </select>
            </div>

            <div class="field">
              <label for="dateOfBirth">Date of birth <span aria-hidden="true">*</span><span class="sr-only">required</span></label>
              <input
                class="input"
                id="dateOfBirth"
                name="dateOfBirth"
                type="date"
                [(ngModel)]="form.dateOfBirth"
                required
              />
            </div>

            <div class="field">
              <label for="gautraId">Gautra</label>
              <select class="input" id="gautraId" name="gautraId" [(ngModel)]="form.gautraId">
                <option [ngValue]="null">Not specified</option>
                @for (option of gautras(); track option.id) {
                  <option [ngValue]="option.id">{{ option.name }}</option>
                }
              </select>
            </div>

            <div class="field">
              <label for="professionId">Profession</label>
              <select class="input" id="professionId" name="professionId" [(ngModel)]="form.professionId">
                <option [ngValue]="null">Not specified</option>
                @for (option of professions(); track option.id) {
                  <option [ngValue]="option.id">{{ option.label }}</option>
                }
              </select>
            </div>

            <div class="field">
              <label for="qualificationId">Qualification</label>
              <select class="input" id="qualificationId" name="qualificationId" [(ngModel)]="form.qualificationId">
                <option [ngValue]="null">Not specified</option>
                @for (option of qualifications(); track option.id) {
                  <option [ngValue]="option.id">{{ option.label }}</option>
                }
              </select>
            </div>

            <div class="field">
              <label for="cityId">City/Village</label>
              <select
                class="input"
                id="cityId"
                name="cityId"
                [(ngModel)]="form.cityId"
                (ngModelChange)="onCityChange($event)"
              >
                <option [ngValue]="null">Not specified</option>
                @for (option of cities(); track option.id) {
                  <option [ngValue]="option.id">{{ option.name }}</option>
                }
              </select>
            </div>

            <div class="field">
              <label for="localityId">Locality</label>
              <select class="input" id="localityId" name="localityId" [(ngModel)]="form.localityId">
                <option [ngValue]="null">Not specified</option>
                @for (option of localities(); track option.id) {
                  <option [ngValue]="option.id">{{ option.name }}</option>
                }
              </select>
            </div>

            <div class="field">
              <label for="heightCm">Height (cm)</label>
              <input class="input" id="heightCm" name="heightCm" type="number" min="0" [(ngModel)]="form.heightCm" />
            </div>

            <div class="field">
              <label for="maritalStatusId">Marital status</label>
              <select class="input" id="maritalStatusId" name="maritalStatusId" [(ngModel)]="form.maritalStatusId">
                <option [ngValue]="null">Not specified</option>
                @for (option of maritalStatuses(); track option.id) {
                  <option [ngValue]="option.id">{{ option.label }}</option>
                }
              </select>
            </div>

            <div class="field">
              <label for="diet">Diet</label>
              <select class="input" id="diet" name="diet" [(ngModel)]="form.diet">
                <option [ngValue]="null">Not specified</option>
                @for (option of diets; track option) {
                  <option [ngValue]="option">{{ dietLabel(option) }}</option>
                }
              </select>
            </div>

            <div class="field">
              <label for="familyType">Family type</label>
              <select class="input" id="familyType" name="familyType" [(ngModel)]="form.familyType">
                <option [ngValue]="null">Not specified</option>
                @for (option of familyTypes; track option) {
                  <option [ngValue]="option">{{ option }}</option>
                }
              </select>
            </div>

            <div class="field">
              <label for="fatherOccupation">Father's occupation</label>
              <input
                class="input"
                id="fatherOccupation"
                name="fatherOccupation"
                [(ngModel)]="form.fatherOccupation"
                maxlength="120"
              />
            </div>

            <div class="field">
              <label for="motherOccupation">Mother's occupation</label>
              <input
                class="input"
                id="motherOccupation"
                name="motherOccupation"
                [(ngModel)]="form.motherOccupation"
                maxlength="120"
              />
            </div>

            <div class="field">
              <label for="siblingsCount">Number of siblings</label>
              <input
                class="input"
                id="siblingsCount"
                name="siblingsCount"
                type="number"
                min="0"
                [(ngModel)]="form.siblingsCount"
              />
            </div>

            <div class="field">
              <label for="preferredAgeMin">Preferred age - from</label>
              <input
                class="input"
                id="preferredAgeMin"
                name="preferredAgeMin"
                type="number"
                min="18"
                [(ngModel)]="form.preferredAgeMin"
              />
            </div>

            <div class="field">
              <label for="preferredAgeMax">Preferred age - to</label>
              <input
                class="input"
                id="preferredAgeMax"
                name="preferredAgeMax"
                type="number"
                min="18"
                [(ngModel)]="form.preferredAgeMax"
              />
            </div>

            <label class="span-all">
              <input type="checkbox" name="manglik" [(ngModel)]="form.manglik" />
              Manglik
            </label>

            <div class="field span-all">
              <label for="horoscopeNotes">Horoscope notes</label>
              <textarea
                class="input"
                id="horoscopeNotes"
                name="horoscopeNotes"
                rows="3"
                maxlength="500"
                [(ngModel)]="form.horoscopeNotes"
              ></textarea>
            </div>

            <div class="field span-all">
              <label for="aboutMe">About me</label>
              <textarea
                class="input"
                id="aboutMe"
                name="aboutMe"
                rows="4"
                maxlength="1000"
                [(ngModel)]="form.aboutMe"
              ></textarea>
            </div>

            <div class="field span-all">
              <label for="partnerPreferenceNotes">Partner preference notes</label>
              <textarea
                class="input"
                id="partnerPreferenceNotes"
                name="partnerPreferenceNotes"
                rows="3"
                maxlength="500"
                [(ngModel)]="form.partnerPreferenceNotes"
              ></textarea>
            </div>

            @if (!myBiodata()) {
              @if (notice(); as consentNotice) {
                <label class="consent-line span-all">
                  <input type="checkbox" name="consentGiven" [(ngModel)]="consentGiven" required />
                  {{ consentNotice.attestation }}
                </label>
              }
            }

            <div class="actions span-all">
              <button class="btn" type="submit" [disabled]="saving() || optInForm.invalid">
                {{ saving() ? 'Saving…' : myBiodata() ? 'Save changes' : 'Opt in' }}
              </button>
              @if (myBiodata()) {
                <button class="btn secondary" type="button" (click)="cancelEditing()">Cancel</button>
              }
              @if (optInForm.invalid && optInForm.touched) {
                <p class="notice error" role="alert">
                  Fill in every required field - full name, gender and date of birth - before saving.
                </p>
              }
            </div>
          </div>
        </form>
        }

        @if (myBiodata(); as mine) {
          <div class="card">
            <h2>Withdraw from matrimony</h2>
            <p class="small">
              Takes your biodata out of search immediately. Proposals already sent or received are
              kept, because they are a record between you and the other member rather than yours
              alone. You can opt in again at any time.
            </p>

            @if (withdrawError(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            @if (mine.isActive) {
              @if (confirmingWithdraw()) {
                <p class="small" role="status">Are you sure you want to withdraw?</p>
                <div class="actions">
                  <button class="btn danger" type="button" [disabled]="saving()" (click)="withdraw()">
                    Yes, withdraw
                  </button>
                  <button class="btn secondary" type="button" (click)="confirmingWithdraw.set(false)">
                    Cancel
                  </button>
                </div>
              } @else {
                <button
                  class="btn secondary"
                  type="button"
                  [attr.aria-expanded]="confirmingWithdraw()"
                  (click)="confirmingWithdraw.set(true)"
                >
                  Withdraw from matrimony
                </button>
              }
            } @else {
              <p class="small">You have already withdrawn.</p>
            }
          </div>
        }
      }
    </div>
  `,
})
export class MatrimonyOptInComponent implements OnInit {
  private readonly api = inject(MatrimonyApi);
  private readonly membersApi = inject(MembersApi);
  private readonly masterData = inject(MasterDataApi);

  readonly genders: readonly Gender[] = ['Male', 'Female', 'Other'];
  readonly diets = Diets;
  readonly familyTypes = FamilyTypes;

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly myBiodata = signal<MyBiodata | null>(null);
  readonly notice = signal<MatrimonyConsentNotice | null>(null);

  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly saved = signal(false);

  readonly withdrawError = signal<string | null>(null);
  readonly confirmingWithdraw = signal(false);

  /** Unknown until matrimony-service answers - the form is not offered until it says Eligible. */
  readonly eligibility = signal<MatrimonyEligibility>('Unknown');

  /** Whether an existing biodata is open in the form; it opens read-only. */
  readonly editing = signal(false);

  readonly notSet = 'Not specified';

  readonly gautras = signal<readonly Gautra[]>([]);
  readonly professions = signal<readonly LookupItem[]>([]);
  readonly qualifications = signal<readonly LookupItem[]>([]);
  readonly maritalStatuses = signal<readonly LookupItem[]>([]);
  readonly cities = signal<readonly City[]>([]);
  readonly localities = signal<readonly Locality[]>([]);

  consentGiven = false;

  form: BiodataFields = this.blankForm();

  ngOnInit(): void {
    this.masterData.listGautras().subscribe({ next: (found) => this.gautras.set(found), error: () => {} });
    this.masterData.listProfessions().subscribe({ next: (found) => this.professions.set(found), error: () => {} });
    this.masterData
      .listQualifications()
      .subscribe({ next: (found) => this.qualifications.set(found), error: () => {} });
    this.masterData
      .listMaritalStatuses()
      .subscribe({ next: (found) => this.maritalStatuses.set(found), error: () => {} });
    this.masterData.listCities().subscribe({ next: (found) => this.cities.set(found), error: () => {} });

    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.editing.set(false);

    this.api.eligibility().subscribe({
      next: (found) => this.eligibility.set(found.status),
      error: () => this.eligibility.set('Unknown'),
    });

    this.api.myBiodata().subscribe({
      next: (mine) => {
        this.myBiodata.set(mine);

        if (mine) {
          this.fill(mine);
          this.loading.set(false);
        } else {
          this.prefillFromProfile();
        }
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  /** Never opted in: fetch the consent notice and the base profile to seed the form. */
  private prefillFromProfile(): void {
    this.api.consentNotice().subscribe({
      next: (found) => this.notice.set(found),
      error: () => {},
    });

    this.membersApi.me().subscribe({
      next: (me) => {
        this.form = {
          ...this.blankForm(),
          fullName: me.fullName,
          // 'Unspecified' is a valid value on the wire but not one of the
          // three choices this form offers - a matrimony profile needs a
          // firm answer, so an unset base profile seeds Male rather than a
          // select with nothing chosen (see this app's own note on an
          // uninitialised `<select>` rendering empty).
          gender: me.gender === 'Unspecified' ? 'Male' : me.gender,
          dateOfBirth: me.dateOfBirth ?? '',
          gautraId: me.gautraId,
          professionId: me.professionId,
          qualificationId: me.qualificationId,
          cityId: me.permanentAddress.cityId,
          localityId: me.permanentAddress.localityId,
        };

        if (this.form.cityId) {
          this.onCityChange(this.form.cityId);
        }

        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  private fill(mine: MyBiodata): void {
    const biodata = mine.biodata;

    this.form = {
      fullName: biodata.fullName,
      gender: biodata.gender === 'Unspecified' ? 'Male' : biodata.gender,
      dateOfBirth: mine.dateOfBirth,
      gautraId: biodata.gautraId,
      professionId: biodata.professionId,
      qualificationId: biodata.qualificationId,
      cityId: biodata.cityId,
      localityId: biodata.localityId,
      heightCm: biodata.heightCm,
      maritalStatusId: biodata.maritalStatusId,
      diet: biodata.diet,
      familyType: biodata.familyType,
      fatherOccupation: biodata.fatherOccupation,
      motherOccupation: biodata.motherOccupation,
      siblingsCount: biodata.siblingsCount,
      manglik: biodata.manglik,
      horoscopeNotes: biodata.horoscopeNotes,
      aboutMe: biodata.aboutMe,
      preferredAgeMin: biodata.preferredAgeMin,
      preferredAgeMax: biodata.preferredAgeMax,
      partnerPreferenceNotes: biodata.partnerPreferenceNotes,
    };

    if (this.form.cityId) {
      this.onCityChange(this.form.cityId);
    }
  }

  onCityChange(cityId: string | null): void {
    this.masterData.listLocalities(cityId).subscribe({
      next: (found) => this.localities.set(found),
      error: () => this.localities.set([]),
    });
  }

  save(): void {
    this.saving.set(true);
    this.saveError.set(null);
    this.saved.set(false);

    const mine = this.myBiodata();

    const request = mine
      ? this.api.update(this.form)
      : this.api.optIn(this.form, this.consentGiven, this.notice()?.version ?? '');

    request.subscribe({
      next: (updated) => {
        this.myBiodata.set(updated);
        this.saving.set(false);
        this.saved.set(true);
        this.editing.set(false);
      },
      error: (failure: unknown) => {
        this.saving.set(false);
        this.saveError.set(describeError(failure));
      },
    });
  }

  withdraw(): void {
    this.saving.set(true);
    this.withdrawError.set(null);

    this.api.withdrawConsent().subscribe({
      next: () => {
        this.saving.set(false);
        this.confirmingWithdraw.set(false);
        this.load();
      },
      error: (failure: unknown) => {
        this.saving.set(false);
        this.withdrawError.set(describeError(failure));
      },
    });
  }

  dietLabel(diet: Diet): string {
    return DietLabels[diet];
  }

  startEditing(): void {
    const mine = this.myBiodata();

    if (mine) {
      this.fill(mine);
    }

    this.saved.set(false);
    this.saveError.set(null);
    this.editing.set(true);
  }

  cancelEditing(): void {
    this.editing.set(false);
    this.saveError.set(null);
  }

  /** A picked id's name, or "Not specified" - never the id itself. */
  nameIn(options: readonly { id: string; name: string }[], id: string | null): string {
    return (id && options.find((o) => o.id === id)?.name) || this.notSet;
  }

  labelIn(options: readonly { id: string; label: string }[], id: string | null): string {
    return (id && options.find((o) => o.id === id)?.label) || this.notSet;
  }

  private blankForm(): BiodataFields {
    return {
      fullName: '',
      gender: 'Male',
      dateOfBirth: '',
      gautraId: null,
      professionId: null,
      qualificationId: null,
      cityId: null,
      localityId: null,
      heightCm: null,
      maritalStatusId: null,
      diet: null,
      familyType: null,
      fatherOccupation: null,
      motherOccupation: null,
      siblingsCount: null,
      manglik: null,
      horoscopeNotes: null,
      aboutMe: null,
      preferredAgeMin: null,
      preferredAgeMax: null,
      partnerPreferenceNotes: null,
    };
  }
}
