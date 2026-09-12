import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { describeError, formatRupees, parseRupees, toInputValue } from '@samaajconnect/shared';
import { MasterDataApi } from '../members/master-data.api';
import { Locality, LookupItem } from '../members/members.models';
import { BusinessesApi } from './businesses.api';
import { Business, BusinessFields, Product, ProductFields } from './businesses.models';

/**
 * Manage business, `/businesses/mine/:id` - edit fields, activate/deactivate,
 * and product management. Deactivating is reversible and says so, the same
 * shape volunteer-groups-service's own "stand a group down" already uses,
 * confirmed inline with the trigger staying in the DOM (never swapped for
 * the confirmation) per this app's own accessibility note on that pattern.
 */
@Component({
  selector: 'app-manage-business',
  imports: [FormsModule, RouterLink],
  styleUrl: './businesses.css',
  template: `
    <div class="businesses-page">
      <a class="back" routerLink="/businesses/mine">‹ Back to My businesses</a>

      @if (loading()) {
        <p role="status">Loading…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (business(); as found) {
        <header class="page-head">
          <div>
            <h1 class="page-title">{{ found.name }}</h1>
            <span class="pill" [class.warn]="!found.isActive">{{ found.isActive ? 'Active' : 'Inactive' }}</span>
          </div>
        </header>

        @if (statusError(); as message) {
          <p class="notice error" role="alert">{{ message }}</p>
        }

        <div class="card">
          <h2>Details</h2>

          <form (ngSubmit)="save()">
            <div class="form-grid">
              <div class="field">
                <label for="name">Name</label>
                <input class="input" id="name" name="name" maxlength="200" [(ngModel)]="form.name" />
              </div>

              <div class="field">
                <label for="categoryId">Category</label>
                <select class="input" id="categoryId" name="categoryId" [(ngModel)]="form.categoryId">
                  @for (option of categories(); track option.id) {
                    <option [value]="option.id">{{ option.label }}</option>
                  }
                </select>
              </div>

              <div class="field">
                <label for="localityId">Locality</label>
                <select class="input" id="localityId" name="localityId" [(ngModel)]="form.localityId">
                  <option [ngValue]="null">Not specified</option>
                  @for (option of localities(); track option.id) {
                    <option [value]="option.id">{{ option.name }}</option>
                  }
                </select>
              </div>

              <div class="field">
                <label for="contactPhone">Contact phone</label>
                <input class="input" id="contactPhone" name="contactPhone" maxlength="20" [(ngModel)]="form.contactPhone" />
              </div>

              <div class="field">
                <label for="contactEmail">Contact email</label>
                <input class="input" id="contactEmail" name="contactEmail" maxlength="200" [(ngModel)]="form.contactEmail" />
              </div>

              <div class="field span-all">
                <label for="description">Description</label>
                <textarea
                  class="input"
                  id="description"
                  name="description"
                  rows="3"
                  maxlength="1000"
                  [(ngModel)]="form.description"
                ></textarea>
              </div>

              <div class="actions span-all">
                <button class="btn" type="submit" [disabled]="saving()">
                  {{ saving() ? 'Saving…' : 'Save changes' }}
                </button>
              </div>
            </div>
          </form>

          @if (!confirmingStatus()) {
            <div class="actions">
              @if (found.isActive) {
                <button class="btn small secondary" type="button" (click)="confirmingStatus.set(true)">
                  Deactivate
                </button>
              } @else {
                <button class="btn small" type="button" [disabled]="statusBusy()" (click)="setActive(true)">
                  Reactivate
                </button>
              }
            </div>
          } @else {
            <div class="notice" role="status">
              <p>
                Deactivating takes {{ found.name }} out of the directory. Every product and past order is
                kept, and this can be undone at any time.
              </p>
              <div class="actions">
                <button class="btn small secondary" type="button" [disabled]="statusBusy()" (click)="setActive(false)">
                  Deactivate {{ found.name }}
                </button>
                <button class="btn small secondary" type="button" (click)="confirmingStatus.set(false)">Cancel</button>
              </div>
            </div>
          }
        </div>

        <div class="card">
          <h2>Products</h2>

          @if (found.products.length === 0) {
            <p class="notice info" role="status">No products listed yet.</p>
          } @else {
            @for (product of found.products; track product.id) {
              <div class="product-row">
                <div>
                  <b>{{ product.name }}</b>
                  @if (product.description; as description) {
                    <div class="small">{{ description }}</div>
                  }
                  <div class="small">{{ formatPrice(product.priceInPaise) }}</div>
                </div>
                <div class="actions">
                  <button class="btn small secondary" type="button" (click)="startEditProduct(product)">Edit</button>
                  <button
                    class="btn small secondary"
                    type="button"
                    [disabled]="productBusy()"
                    (click)="toggleAvailability(product)"
                  >
                    {{ product.isAvailable ? 'Take off sale' : 'Put back on sale' }}
                  </button>
                </div>
              </div>
            }
          }

          @if (productError(); as message) {
            <p class="notice error" role="alert">{{ message }}</p>
          }

          <form (ngSubmit)="saveProduct(found.id)">
            <h3>{{ editingProductId() === null ? 'Add a product' : 'Edit product' }}</h3>
            <div class="form-grid">
              <div class="field">
                <label for="productName">Name</label>
                <input class="input" id="productName" name="productName" maxlength="200" [(ngModel)]="productForm.name" />
              </div>

              <div class="field">
                <label for="productPrice">Price (₹)</label>
                <input class="input" id="productPrice" name="productPrice" [(ngModel)]="productPriceInput" />
              </div>

              <div class="field span-all">
                <label for="productDescription">Description</label>
                <textarea
                  class="input"
                  id="productDescription"
                  name="productDescription"
                  rows="2"
                  maxlength="500"
                  [(ngModel)]="productForm.description"
                ></textarea>
              </div>

              <div class="actions span-all">
                <button class="btn small" type="submit" [disabled]="productBusy()">
                  {{ editingProductId() === null ? 'Add product' : 'Save product' }}
                </button>
                @if (editingProductId() !== null) {
                  <button class="btn small secondary" type="button" (click)="cancelEditProduct()">Cancel</button>
                }
              </div>
            </div>
          </form>
        </div>
      }
    </div>
  `,
})
export class ManageBusinessComponent implements OnInit {
  private readonly api = inject(BusinessesApi);
  private readonly route = inject(ActivatedRoute);
  private readonly masterData = inject(MasterDataApi);

  readonly business = signal<Business | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly categories = signal<readonly LookupItem[]>([]);
  readonly localities = signal<readonly Locality[]>([]);

  readonly saving = signal(false);
  readonly confirmingStatus = signal(false);
  readonly statusBusy = signal(false);
  readonly statusError = signal<string | null>(null);

  readonly productBusy = signal(false);
  readonly productError = signal<string | null>(null);
  readonly editingProductId = signal<string | null>(null);

  form: BusinessFields = {
    name: '',
    categoryId: '',
    cityId: null,
    localityId: null,
    description: null,
    contactPhone: null,
    contactEmail: null,
  };

  productForm: ProductFields = { name: '', description: null, priceInPaise: 0 };
  productPriceInput = '';

  ngOnInit(): void {
    this.masterData
      .listBusinessCategories()
      .subscribe({ next: (found) => this.categories.set(found), error: () => {} });
    this.masterData.listLocalities().subscribe({ next: (found) => this.localities.set(found), error: () => {} });

    this.load();
  }

  private businessId(): string | null {
    return this.route.snapshot.paramMap.get('id');
  }

  load(): void {
    const id = this.businessId();

    if (id === null) {
      this.loading.set(false);
      this.error.set('That link does not name a business.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.api.get(id).subscribe({
      next: (found) => {
        this.business.set(found);
        this.form = {
          name: found.name,
          categoryId: found.categoryId,
          cityId: found.cityId,
          localityId: found.localityId,
          description: found.description,
          contactPhone: found.contactPhone,
          contactEmail: found.contactEmail,
        };
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  save(): void {
    const id = this.businessId();

    if (id === null) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);

    this.api.update(id, this.form).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.business.set(updated);
      },
      error: (failure: unknown) => {
        this.saving.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  setActive(active: boolean): void {
    const id = this.businessId();

    if (id === null) {
      return;
    }

    this.statusBusy.set(true);
    this.statusError.set(null);

    this.api.setStatus(id, active).subscribe({
      next: (updated) => {
        this.statusBusy.set(false);
        this.confirmingStatus.set(false);
        this.business.set(updated);
      },
      error: (failure: unknown) => {
        this.statusBusy.set(false);
        this.statusError.set(describeError(failure));
      },
    });
  }

  startEditProduct(product: Product): void {
    this.editingProductId.set(product.id);
    this.productForm = { name: product.name, description: product.description, priceInPaise: product.priceInPaise };
    this.productPriceInput = toInputValue(product.priceInPaise);
  }

  cancelEditProduct(): void {
    this.editingProductId.set(null);
    this.productForm = { name: '', description: null, priceInPaise: 0 };
    this.productPriceInput = '';
  }

  saveProduct(businessId: string): void {
    const priceInPaise = parseRupees(this.productPriceInput);

    if (this.productForm.name.trim().length === 0 || priceInPaise === null || priceInPaise <= 0) {
      this.productError.set('Enter a name and a price greater than zero.');
      return;
    }

    const fields: ProductFields = { ...this.productForm, priceInPaise };

    this.productBusy.set(true);
    this.productError.set(null);

    const editingId = this.editingProductId();

    const request = editingId === null
      ? this.api.addProduct(businessId, fields)
      : this.api.updateProduct(businessId, editingId, fields);

    request.subscribe({
      next: (updated) => {
        this.productBusy.set(false);
        this.business.set(updated);
        this.cancelEditProduct();
      },
      error: (failure: unknown) => {
        this.productBusy.set(false);
        this.productError.set(describeError(failure));
      },
    });
  }

  toggleAvailability(product: Product): void {
    const found = this.business();

    if (found === null) {
      return;
    }

    this.productBusy.set(true);
    this.productError.set(null);

    this.api.setProductAvailability(found.id, product.id, !product.isAvailable).subscribe({
      next: (updated) => {
        this.productBusy.set(false);
        this.business.set(updated);
      },
      error: (failure: unknown) => {
        this.productBusy.set(false);
        this.productError.set(describeError(failure));
      },
    });
  }

  formatPrice(paise: number): string {
    return formatRupees(paise);
  }
}
