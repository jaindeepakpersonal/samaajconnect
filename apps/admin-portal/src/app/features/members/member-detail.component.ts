import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthedImageDirective, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import {
  AdminAddress,
  AdminMember,
  AdminMemberGroup,
  AdminMemberHousehold,
  City,
  Gautra,
  LookupItem,
  Locality,
} from '../../core/admin.models';

/**
 * One member, and the correction form.
 *
 * **The endpoint this calls was written for this screen, because the one that
 * already existed could not be used.** `PATCH /v1/members/{id}` accepted a
 * Samaaj administrator and replaced the profile whole, so it required the
 * member's privacy levels and whether they are listed — and no read available to
 * an administrator returns either. Correcting a misspelt name therefore meant
 * sending settings the caller had no way to know, and an unreadable level parses
 * as Private, so the likeliest accident was quietly hiding every field the
 * member had chosen to share. `PATCH /v1/members/{id}/details` carries no
 * privacy field at all.
 *
 * That is why this form has no privacy controls and no "listed in the
 * directory" tick. They are not omitted for space: an administrator cannot set
 * them, and a control that cannot be saved is worse than an absent one. The
 * note under the form says so rather than leaving it to be discovered.
 *
 * **Every field is shown filled in, including ones the member marked Private.**
 * `IsVisibleTo` lets a Samaaj admin past every level — correcting details is
 * the job — so unlike the member portal's directory, a null here means "not
 * set" and never "not shared". The form must send the values back unchanged, so
 * showing them is not a courtesy, it is what stops a correction from wiping a
 * field the administrator was never shown.
 *
 * **Gautra, Qualification, and Permanent/Temporary address all became
 * master-data-service picks on 2026-09-11, matching the member portal's own
 * My Profile screen field for field.** Locality's City→Locality cascade is
 * the same shape there: choosing a City clears whatever Locality was
 * previously selected and loads that City's own list, since a Locality from
 * the old City is nonsense once the City changes. "Same as permanent" is a
 * one-time copy into Temporary, not a link the two stay bound by afterwards.
 *
 * **It opens as the member's page since 2026-10-24, and corrects only on
 * request.** Every member list in this panel links here, and most visits are
 * to look somebody up, not to change them - so the page shows the member as a
 * profile (photo, name with Gautra, details, address, household and volunteer
 * groups) and "Correct details" opens the form. Cancel puts back what is on
 * file. It follows the route, so a household link from one member to another
 * reloads rather than showing the first member under the second's address.
 */
@Component({
  selector: 'app-member-detail',
  imports: [AuthedImageDirective, FormsModule, RouterLink],
  template: `
    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else if (member(); as person) {
      <div class="member-head">
        @if (person.photoUrl; as path) {
          <img class="member-avatar" [scAuthedSrc]="path" [alt]="'Photo of ' + person.fullName" />
        } @else {
          <span class="member-avatar initials" aria-hidden="true">{{ initials(person.fullName) }}</span>
        }
        <div class="member-head-text">
          <h1 class="title">
            {{ person.fullName }}
            @if (nameOf(gautras(), person.gautraId); as gautra) {
              <span class="member-gautra">({{ gautra }})</span>
            }
          </h1>
          <p class="sub">{{ headline(person) }}</p>
        </div>
        @if (!editing()) {
          <div class="member-head-actions">
            <button class="btn" type="button" (click)="startEditing()">Correct details</button>
          </div>
        }
      </div>

      @if (saved()) {
        <p class="notice ok" role="status">
          Saved. The change is recorded against your account in the audit log, with the names
          of the fields you changed.
        </p>
      }

      @if (!editing()) {
        <div class="grid2">
          <div class="card">
            <h2>About</h2>
            <dl class="facts">
              <dt>Mobile</dt>
              <dd>{{ person.mobile ?? notSet }}</dd>
              <dt>Email</dt>
              <dd>{{ person.email ?? notSet }}</dd>
              <dt>Date of birth</dt>
              <dd>{{ person.dateOfBirth ?? notSet }}</dd>
              <dt>Gender</dt>
              <dd>{{ person.gender }}</dd>
              <dt>Gautra</dt>
              <dd>{{ nameOf(gautras(), person.gautraId) ?? notSet }}</dd>
              <dt>Profession</dt>
              <dd>{{ labelOf(professions(), person.professionId) ?? notSet }}</dd>
              <dt>Qualification</dt>
              <dd>{{ labelOf(qualifications(), person.qualificationId) ?? notSet }}</dd>
              <dt>Father</dt>
              <dd>{{ person.fatherName ?? notSet }}</dd>
              <dt>Mother</dt>
              <dd>{{ person.motherName ?? notSet }}</dd>
            </dl>
          </div>

          <div class="card">
            <h2>Address</h2>
            <dl class="facts">
              <dt>Permanent</dt>
              <dd>{{ addressText(person.permanentAddress, permanentLocalities()) }}</dd>
              <dt>Temporary</dt>
              <dd>{{ person.temporaryAddress ? addressText(person.temporaryAddress, temporaryLocalities()) : notSet }}</dd>
            </dl>
          </div>

          <div class="card">
            <h2>Household</h2>
            @if (household(); as found) {
              @if (found.household; as home) {
                @if (home.namedAfter; as namedAfter) {
                  <p class="small">{{ namedAfter }}'s household</p>
                }
                <ul class="people">
                  @for (relative of home.members; track relative.memberId) {
                    <li>
                      <a [routerLink]="['/members', relative.memberId]">{{ relative.fullName }}</a>
                      <span class="small">
                        · {{ relative.isAdmin ? 'Family admin' : (relative.relationshipToAdmin ?? 'Member') }}
                        @if (relative.isHead) { · Head }
                      </span>
                    </li>
                  }
                </ul>
              } @else {
                <p class="empty">Not in a household on the platform.</p>
              }
            } @else {
              <p class="empty" role="status">Loading…</p>
            }
          </div>

          <div class="card">
            <h2>Volunteer groups</h2>
            @if (groups(); as found) {
              @if (found.length === 0) {
                <p class="empty">Not in any volunteer group.</p>
              } @else {
                <ul class="people">
                  @for (group of found; track group.groupId) {
                    <li>
                      <a [routerLink]="['/groups', group.groupId]">{{ group.name }}</a>
                      <span class="small"> · {{ group.isPresident ? 'President' : (group.roleTitle ?? 'Member') }}</span>
                    </li>
                  }
                </ul>
              }
            } @else {
              <p class="empty" role="status">Loading…</p>
            }
          </div>
        </div>
      } @else {
      <p class="sub">
        Correcting somebody's details on their behalf. What they share with the Samaaj stays
        their decision.
      </p>

      <div class="card">
        <h2>Details</h2>

        <form (ngSubmit)="save()">
          <div class="field">
            <label for="fullName">Full name</label>
            <input
              class="input"
              id="fullName"
              name="fullName"
              required
              maxlength="200"
              [(ngModel)]="form.fullName"
              [attr.aria-invalid]="nameMissing() ? 'true' : null"
            />
            @if (nameMissing()) {
              <p class="small error-text" role="alert">A member has to have a name.</p>
            }
          </div>

          <div class="form-grid">
            <div class="field">
              <label for="dateOfBirth">Date of birth</label>
              <input
                class="input"
                id="dateOfBirth"
                name="dateOfBirth"
                type="date"
                [(ngModel)]="form.dateOfBirth"
              />
            </div>

            <div class="field">
              <label for="gender">Gender</label>
              <select class="input" id="gender" name="gender" [(ngModel)]="form.gender">
                @for (option of genders; track option) {
                  <option [value]="option">{{ option }}</option>
                }
              </select>
            </div>

            <div class="field">
              <label for="mobile">Mobile</label>
              <input
                class="input"
                id="mobile"
                name="mobile"
                maxlength="20"
                [(ngModel)]="form.mobile"
              />
            </div>

            <div class="field">
              <label for="email">Email</label>
              <input
                class="input"
                id="email"
                name="email"
                maxlength="320"
                [(ngModel)]="form.email"
              />
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

            <div class="field">
              <label for="profession">Profession</label>
              <select class="input" id="profession" name="profession" [(ngModel)]="form.professionId">
                <option value="">Not set</option>
                @for (option of professions(); track option.id) {
                  <option [value]="option.id">{{ option.label }}</option>
                }
              </select>
            </div>

            <div class="field">
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
          </div>

          <h3>Permanent address</h3>

          <div class="field">
            <label for="permanent-line">Address</label>
            <input
              class="input"
              id="permanent-line"
              name="permanentLine"
              maxlength="500"
              [(ngModel)]="form.permanentAddress.line"
            />
          </div>

          <div class="form-grid">
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

          <h3>Temporary address</h3>

          <div class="field">
            <label for="same-as-permanent">
              <input
                id="same-as-permanent"
                type="checkbox"
                name="sameAsPermanent"
                [ngModel]="sameAsPermanent()"
                (ngModelChange)="toggleSameAsPermanent($event)"
              />
              Same as permanent
            </label>
          </div>

          <div class="field">
            <label for="temporary-line">Address</label>
            <input
              class="input"
              id="temporary-line"
              name="temporaryLine"
              maxlength="500"
              [(ngModel)]="form.temporaryAddress.line"
              [disabled]="sameAsPermanent()"
            />
          </div>

          <div class="form-grid">
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

          <p class="small">
            You are shown every field, including any this member marked private, because
            correcting them is what this screen is for. What they share, and whether they
            appear in the member directory, are theirs to change and cannot be changed here.
          </p>

          <div class="actions">
            <button class="btn" type="submit" [disabled]="saving()">
              {{ saving() ? 'Saving…' : 'Save corrections' }}
            </button>
            <button class="btn alt" type="button" (click)="cancelEditing()">Cancel</button>
          </div>
        </form>
      </div>
      }
    }
  `,
  styles: `
    .field {
      display: flex;
      flex-direction: column;
      gap: 4px;
      margin-bottom: 12px;
    }

    .error-text {
      color: var(--danger-text, #b00020);
    }

    .member-head {
      display: flex;
      gap: 16px;
      align-items: center;
      flex-wrap: wrap;
      margin: 12px 0 16px;
    }

    .member-avatar {
      width: 96px;
      height: 96px;
      border-radius: 50%;
      object-fit: cover;
      flex-shrink: 0;
      background: var(--soft);
    }

    .member-avatar.initials {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      font-size: 2rem;
      font-weight: 700;
      color: var(--accent-ink);
    }

    .member-head-text {
      flex: 1 1 240px;
      min-width: 0;
    }

    .member-head-text .title {
      margin: 0;
      overflow-wrap: anywhere;
    }

    .member-gautra {
      font-weight: 400;
      color: var(--muted);
    }

    .member-head-actions {
      display: flex;
      gap: 8px;
    }

    .grid2 {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
      gap: 16px;
    }

    .grid2 .card {
      margin: 0;
    }

    .facts {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 6px 16px;
      margin: 0;
    }

    .facts dt {
      font-weight: 600;
    }

    .facts dd {
      margin: 0;
      color: var(--muted);
      overflow-wrap: anywhere;
    }

    .people {
      list-style: none;
      margin: 0;
      padding: 0;
    }

    .people li {
      padding: 6px 0;
    }

    .people li + li {
      border-top: 1px solid var(--line);
    }
  `,
})
export class MemberDetailComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  readonly member = signal<AdminMember | null>(null);

  /** View mode until "Correct details" - most visits look somebody up. */
  readonly editing = signal(false);
  readonly household = signal<AdminMemberHousehold | null>(null);
  readonly groups = signal<readonly AdminMemberGroup[] | null>(null);
  readonly notSet = 'Not set';
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly saved = signal(false);
  readonly error = signal<string | null>(null);

  readonly gautras = signal<readonly Gautra[]>([]);
  readonly professions = signal<readonly LookupItem[]>([]);
  readonly qualifications = signal<readonly LookupItem[]>([]);
  readonly cities = signal<readonly City[]>([]);
  readonly permanentLocalities = signal<readonly Locality[]>([]);
  readonly temporaryLocalities = signal<readonly Locality[]>([]);

  /** True while the Temporary fields are a disabled copy of Permanent. */
  readonly sameAsPermanent = signal(false);

  /** The service's closed list. A value outside it is a validation problem. */
  readonly genders = ['Unspecified', 'Male', 'Female', 'Other'];

  form = {
    fullName: '',
    dateOfBirth: '',
    gender: 'Unspecified',
    mobile: '',
    email: '',
    gautraId: '',
    professionId: '',
    qualificationId: '',
    permanentAddress: { line: '', cityId: '', localityId: '' },
    temporaryAddress: { line: '', cityId: '', localityId: '' },
  };

  nameMissing(): boolean {
    return this.form.fullName.trim() === '';
  }

  ngOnInit(): void {
    this.api.listGautras().subscribe({ next: (found) => this.gautras.set(found), error: () => {} });
    this.api.listLookupItems('ProfessionType').subscribe({
      next: (found) => this.professions.set(found),
      error: () => {},
    });
    this.api.listLookupItems('Qualification').subscribe({
      next: (found) => this.qualifications.set(found),
      error: () => {},
    });
    this.api.listCities().subscribe({ next: (found) => this.cities.set(found), error: () => {} });

    // A household link from one member to another reuses this component, so
    // follow the id rather than reading it once.
    this.route.paramMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => this.load(params.get('id')));
  }

  private load(id: string | null): void {
    if (id === null) {
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.editing.set(false);
    this.saved.set(false);
    this.error.set(null);
    this.household.set(null);
    this.groups.set(null);

    this.api.member(id).subscribe({
      next: (person) => {
        this.member.set(person);
        this.fill(person);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });

    // Either failing leaves its card saying so rather than breaking the page.
    this.api.memberHousehold(id).subscribe({
      next: (found) => this.household.set(found),
      error: () => this.household.set({ household: null }),
    });

    this.api.memberGroups(id).subscribe({
      next: (found) => this.groups.set(found),
      error: () => this.groups.set([]),
    });
  }

  startEditing(): void {
    this.saved.set(false);
    this.editing.set(true);
  }

  /** Back to the page, putting back what is on file. */
  cancelEditing(): void {
    const person = this.member();

    if (person !== null) {
      this.fill(person);
    }

    this.error.set(null);
    this.editing.set(false);
  }

  initials(name: string): string {
    return name
      .split(/\s+/)
      .filter((part) => part.length > 0)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join('');
  }

  nameOf(list: readonly { id: string; name: string }[], id: string | null): string | null {
    return id === null ? null : (list.find((item) => item.id === id)?.name ?? null);
  }

  labelOf(list: readonly LookupItem[], id: string | null): string | null {
    return id === null ? null : (list.find((item) => item.id === id)?.label ?? null);
  }

  /** Profession and locality, under the name, when set. */
  headline(person: AdminMember): string {
    return [
      this.labelOf(this.professions(), person.professionId),
      this.nameOf(this.permanentLocalities(), person.permanentAddress.localityId),
    ]
      .filter((part): part is string => part !== null)
      .join(' · ');
  }

  addressText(address: AdminAddress, localities: readonly Locality[]): string {
    const parts = [
      address.line,
      this.nameOf(localities, address.localityId),
      this.nameOf(this.cities(), address.cityId),
    ].filter((part): part is string => typeof part === 'string' && part.trim().length > 0);

    return parts.length === 0 ? this.notSet : parts.join(', ');
  }

  private fill(person: AdminMember): void {
    // Empty strings rather than nulls, because an `undefined` bound to an input
    // renders as the string "undefined" and an `undefined` bound to a `<select>`
    // matches no option at all - the control renders blank rather than showing
    // its first choice, which is the member portal's Pathshala picker bug.
    this.form = {
      fullName: person.fullName,
      dateOfBirth: person.dateOfBirth ?? '',
      gender: person.gender,
      mobile: person.mobile ?? '',
      email: person.email ?? '',
      gautraId: person.gautraId ?? '',
      professionId: person.professionId ?? '',
      qualificationId: person.qualificationId ?? '',
      permanentAddress: toFormAddress(person.permanentAddress),
      temporaryAddress: toFormAddress(person.temporaryAddress),
    };

    this.sameAsPermanent.set(false);

    if (this.form.permanentAddress.cityId) {
      this.loadLocalities('permanent', this.form.permanentAddress.cityId);
    }

    if (this.form.temporaryAddress.cityId) {
      this.loadLocalities('temporary', this.form.temporaryAddress.cityId);
    }
  }

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
    this.api.listLocalities(cityId).subscribe({
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

  save(): void {
    const person = this.member();

    if (person === null || this.nameMissing()) {
      return;
    }

    this.saving.set(true);
    this.saved.set(false);
    this.error.set(null);

    const permanentAddress = toWireAddress(this.form.permanentAddress);
    const temporaryAddress = isBlankAddress(this.form.temporaryAddress)
      ? null
      : toWireAddress(this.form.temporaryAddress);

    this.api
      .correctMember(person.id, {
        fullName: this.form.fullName.trim(),
        // Blank means "cleared", which is a real correction: a wrong number an
        // administrator is asked to remove has to be removable. The service
        // normalises whitespace to null, so an empty box and a box of spaces
        // are the same thing.
        dateOfBirth: this.blank(this.form.dateOfBirth),
        gender: this.form.gender,
        mobile: this.blank(this.form.mobile),
        email: this.blank(this.form.email),
        gautraId: this.blank(this.form.gautraId),
        professionId: this.blank(this.form.professionId),
        qualificationId: this.blank(this.form.qualificationId),
        permanentAddress,
        temporaryAddress,
      })
      .subscribe({
        next: (updated) => {
          this.member.set(updated);
          this.fill(updated);
          this.saving.set(false);
          this.saved.set(true);
          this.editing.set(false);
        },
        error: (failure: unknown) => {
          this.error.set(describeError(failure));
          this.saving.set(false);
        },
      });
  }

  private blank(value: string): string | null {
    return value.trim() === '' ? null : value.trim();
  }
}

interface FormAddress {
  line: string;
  cityId: string;
  localityId: string;
}

function toFormAddress(address: AdminAddress | null): FormAddress {
  return {
    line: address?.line ?? '',
    cityId: address?.cityId ?? '',
    localityId: address?.localityId ?? '',
  };
}

function toWireAddress(address: FormAddress): AdminAddress {
  return {
    line: address.line.trim() === '' ? null : address.line.trim(),
    cityId: address.cityId === '' ? null : address.cityId,
    localityId: address.localityId === '' ? null : address.localityId,
  };
}

function isBlankAddress(address: FormAddress): boolean {
  return address.line.trim().length === 0 && !address.cityId && !address.localityId;
}
