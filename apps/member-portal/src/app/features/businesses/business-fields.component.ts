import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MasterDataApi } from '../members/master-data.api';
import { City, Locality, LookupItem } from '../members/members.models';
import { BusinessFields } from './businesses.models';

/**
 * A business's own fields - name, category, contact, and where it is - shared
 * by the register form on My businesses and the edit form on Manage business,
 * so the two cannot drift.
 *
 * **Where it is, as a street line plus a City/Village -> Locality cascade**
 * (2026-10-24), the same cascade My Profile's address uses. Choosing a city
 * clears the locality and loads that city's localities only; a locality list
 * for every city on the platform was what the form offered before, which named
 * a place but not which town it was in. Every part is optional.
 *
 * It writes straight into the parent's \`fields\` object, inside the parent's
 * own form, so Save and Register read one object as they always did.
 */
@Component({
  selector: 'app-business-fields',
  imports: [FormsModule],
  styleUrl: './businesses.css',
  template: `
    <div class="form-grid">
      <div class="field">
        <label [for]="prefix() + '-name'">Name</label>
        <input class="input" [id]="prefix() + '-name'" name="name" maxlength="200" [(ngModel)]="f.name" />
      </div>

      <div class="field">
        <label [for]="prefix() + '-category'">Category</label>
        <select class="input" [id]="prefix() + '-category'" name="categoryId" [(ngModel)]="f.categoryId">
          <option value="">Choose a category…</option>
          @for (option of categories(); track option.id) {
            <option [value]="option.id">{{ option.label }}</option>
          }
        </select>
      </div>

      <div class="field span-all">
        <label [for]="prefix() + '-address'">Address</label>
        <input class="input" [id]="prefix() + '-address'" name="addressLine" maxlength="300"
          placeholder="Shop 4, Bapu Bazaar" [(ngModel)]="f.addressLine" />
      </div>

      <div class="field">
        <label [for]="prefix() + '-city'">City or village</label>
        <select class="input" [id]="prefix() + '-city'" name="cityId"
          [ngModel]="f.cityId ?? ''" (ngModelChange)="chooseCity($event)">
          <option value="">Not specified</option>
          @for (city of cities(); track city.id) {
            <option [value]="city.id">{{ city.name }}</option>
          }
        </select>
      </div>

      <div class="field">
        <label [for]="prefix() + '-locality'">Locality</label>
        <select class="input" [id]="prefix() + '-locality'" name="localityId" [disabled]="!f.cityId"
          [ngModel]="f.localityId ?? ''" (ngModelChange)="f.localityId = $event || null">
          <option value="">{{ f.cityId ? 'Not specified' : 'Choose a city first' }}</option>
          @for (locality of localities(); track locality.id) {
            <option [value]="locality.id">{{ locality.name }}</option>
          }
        </select>
      </div>

      <div class="field">
        <label [for]="prefix() + '-phone'">Contact phone</label>
        <input class="input" [id]="prefix() + '-phone'" name="contactPhone" maxlength="20" [(ngModel)]="f.contactPhone" />
      </div>

      <div class="field">
        <label [for]="prefix() + '-email'">Contact email</label>
        <input class="input" [id]="prefix() + '-email'" name="contactEmail" maxlength="200" [(ngModel)]="f.contactEmail" />
      </div>

      <div class="field span-all">
        <label [for]="prefix() + '-description'">Description</label>
        <textarea class="input" [id]="prefix() + '-description'" name="description" rows="3" maxlength="1000"
          [(ngModel)]="f.description"></textarea>
      </div>
    </div>
  `,
})
export class BusinessFieldsComponent implements OnInit {
  private readonly masterData = inject(MasterDataApi);

  /** The parent's own object, edited in place. */
  readonly fields = input.required<BusinessFields>();
  readonly categories = input<readonly LookupItem[]>([]);

  /** Keeps two forms' control ids apart on one page. */
  readonly prefix = input('business');

  readonly cities = signal<readonly City[]>([]);
  readonly localities = signal<readonly Locality[]>([]);

  get f(): BusinessFields {
    return this.fields();
  }

  ngOnInit(): void {
    this.masterData.listCities().subscribe({ next: (found) => this.cities.set(found), error: () => {} });

    // An existing business's locality has to be in the list before the select
    // can show it, or it renders as though none were chosen.
    if (this.f.cityId) {
      this.loadLocalities(this.f.cityId);
    }
  }

  chooseCity(cityId: string): void {
    this.f.cityId = cityId || null;
    this.f.localityId = null;
    this.localities.set([]);

    if (this.f.cityId) {
      this.loadLocalities(this.f.cityId);
    }
  }

  private loadLocalities(cityId: string): void {
    this.masterData.listLocalities(cityId).subscribe({ next: (found) => this.localities.set(found), error: () => {} });
  }
}
