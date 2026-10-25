import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthedImageDirective, describeError, formatRupees, parseRupees, toInputValue } from '@samaajconnect/shared';
import { Observable } from 'rxjs';
import { MasterDataApi } from '../members/master-data.api';
import { City, Locality, LookupItem } from '../members/members.models';
import { BusinessFieldsComponent } from './business-fields.component';
import { initialsOf } from '../members/member-lookup';
import { placeOf } from './business-place';
import { BusinessesApi } from './businesses.api';
import { Business, BusinessFields, Product, ProductFields } from './businesses.models';

/**
 * Manage business, `/businesses/mine/:id` - two tabs, Details and Products,
 * with every form in a popup (2026-10-24, the user's request).
 *
 * **Details** shows the business as it reads - photo, category, where it is,
 * contact - with Edit details (a popup), the photo's own Change and Remove, and
 * Deactivate. Deactivating is reversible and says so, confirmed inline with the
 * trigger staying in the DOM (never swapped for the confirmation) per this
 * app's accessibility note on that pattern.
 *
 * **Products** is the catalogue as tiles with their images, and Add a product
 * and each Edit open the same popup. A product's image is its own request after
 * the product exists, for the reason the business photo is: the upload needs
 * the product's id, and a file is not a JSON field.
 */
@Component({
  selector: 'app-manage-business',
  imports: [FormsModule, RouterLink, AuthedImageDirective, BusinessFieldsComponent],
  styleUrl: './businesses.css',
  template: `
    <div class="businesses-page">
      <a class="back" routerLink="/businesses/mine">‹ Back to My businesses</a>

      @if (loading()) {
        <p role="status">Loading…</p>
      } @else if (loadError(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (business(); as found) {
        <header class="biz-hero card">
          @if (found.photoUrl; as path) {
            <img class="biz-hero-photo" [scAuthedSrc]="path" alt="" />
          } @else {
            <span class="biz-hero-photo placeholder" aria-hidden="true">{{ initials(found.name) }}</span>
          }

          <div class="biz-hero-body">
            <h1 class="page-title">{{ found.name }}</h1>
            <p>
              <span class="pill" [class.warn]="!found.isActive">{{ found.isActive ? 'Active' : 'Inactive' }}</span>
              @if (categoryLabel(found.categoryId); as category) {
                <span class="small">{{ category }}</span>
              }
            </p>
            @if (place(found); as where) {
              <p class="small">{{ where }}</p>
            }
          </div>
        </header>

        @if (error(); as message) {
          @if (!popup()) {
            <p class="notice error" role="alert">{{ message }}</p>
          }
        }

        @if (done(); as message) {
          <p class="notice ok" role="status">{{ message }}</p>
        }

        <div class="tabs" role="tablist" aria-label="This business">
          @for (item of tabs; track item.value) {
            <button
              type="button"
              role="tab"
              [id]="'business-tab-' + item.value"
              [class.on]="tab() === item.value"
              [attr.aria-selected]="tab() === item.value"
              aria-controls="business-tab-panel"
              (click)="tab.set(item.value)"
            >
              {{ item.label }}
              @if (item.value === 'products') {
                ({{ found.products.length }})
              }
            </button>
          }
        </div>

        <div id="business-tab-panel" role="tabpanel" [attr.aria-labelledby]="'business-tab-' + tab()">
          @switch (tab()) {
            @case ('products') {
              <div class="card">
                <div class="section-head">
                  <h2>Products</h2>
                  <button class="btn small" type="button" (click)="openProduct(null)">Add a product</button>
                </div>

                @if (found.products.length === 0) {
                  <p class="notice info" role="status">No products listed yet. Add one, with a picture, to start selling.</p>
                } @else {
                  <ul class="product-grid" aria-label="Your products">
                    @for (product of found.products; track product.id) {
                      <li class="product-card" [class.off-sale]="!product.isAvailable">
                        @if (product.photoUrl; as path) {
                          <img class="product-image" [scAuthedSrc]="path" alt="" />
                        } @else {
                          <span class="product-image placeholder" aria-hidden="true">{{ initials(product.name) }}</span>
                        }
                        <div class="product-body">
                          <h3 class="product-name">{{ product.name }}</h3>
                          <p class="price">{{ formatPrice(product.priceInPaise) }}</p>
                          @if (product.description; as description) {
                            <p class="product-description">{{ description }}</p>
                          }
                          @if (!product.isAvailable) {
                            <span class="pill warn">Off sale</span>
                          }
                          <div class="actions">
                            <button class="btn small secondary" type="button" (click)="openProduct(product)">
                              Edit<span class="sr-only"> {{ product.name }}</span>
                            </button>
                            <button class="btn small secondary" type="button" [disabled]="busy()"
                              (click)="toggleAvailability(product)">
                              {{ product.isAvailable ? 'Take off sale' : 'Put back on sale' }}
                              <span class="sr-only"> {{ product.name }}</span>
                            </button>
                          </div>
                        </div>
                      </li>
                    }
                  </ul>
                }
              </div>
            }

            @default {
              <div class="card">
                <div class="section-head">
                  <h2>Details</h2>
                  <button class="btn small" type="button" (click)="openDetails(found)">Edit details</button>
                </div>

                <dl class="details">
                  <dt>Category</dt>
                  <dd>{{ categoryLabel(found.categoryId) ?? '—' }}</dd>
                  <dt>Where</dt>
                  <dd>{{ place(found) ?? 'Not specified' }}</dd>
                  <dt>Phone</dt>
                  <dd>{{ found.contactPhone ?? '—' }}</dd>
                  <dt>Email</dt>
                  <dd>{{ found.contactEmail ?? '—' }}</dd>
                  <dt>About</dt>
                  <dd class="group-body">{{ found.description ?? '—' }}</dd>
                </dl>
              </div>

              <div class="card spaced">
                <h2>Photo</h2>
                <p class="small">Shown on the directory and across the top of the business's page. JPEG, PNG or WebP, 2 MB or smaller.</p>
                <label for="business-photo">{{ found.photoUrl ? 'Change the photo' : 'Add a photo' }}</label>
                <input id="business-photo" class="input" type="file" accept="image/jpeg,image/png,image/webp"
                  [disabled]="busy()" (change)="uploadPhoto($event)" />
                @if (found.photoUrl) {
                  <div class="actions">
                    <button class="btn small secondary" type="button" [disabled]="busy()" (click)="removePhoto()">
                      Remove the photo
                    </button>
                  </div>
                }
              </div>

              <div class="card spaced">
                <h2>Listing</h2>
                <div class="actions">
                  @if (found.isActive) {
                    <button class="btn small secondary" type="button" [attr.aria-expanded]="confirmingStatus()"
                      (click)="confirmingStatus.set(!confirmingStatus())">
                      Deactivate
                    </button>
                  } @else {
                    <button class="btn small" type="button" [disabled]="busy()" (click)="setActive(true)">
                      Reactivate
                    </button>
                  }
                </div>

                @if (confirmingStatus() && found.isActive) {
                  <div class="notice warn">
                    <p role="status">
                      Deactivating takes {{ found.name }} out of the directory. Every product and past order is
                      kept, and this can be undone at any time.
                    </p>
                    <div class="actions">
                      <button class="btn small secondary" type="button" [disabled]="busy()" (click)="setActive(false)">
                        Deactivate {{ found.name }}
                      </button>
                      <button class="btn small secondary" type="button" (click)="confirmingStatus.set(false)">Cancel</button>
                    </div>
                  </div>
                }
              </div>
            }
          }
        </div>

        <!-- Edit details ------------------------------------------------- -->
        @if (popup() === 'details') {
          <div class="modal-backdrop" (click)="closePopup()">
            <div class="card modal-card modal-dialog" role="dialog" aria-modal="true" aria-labelledby="details-popup-title"
              (click)="$event.stopPropagation()" (keydown.escape)="closePopup()">
              <h2 id="details-popup-title">Edit {{ found.name }}</h2>

              @if (error(); as message) {
                <p class="notice error" role="alert">{{ message }}</p>
              }

              <form (ngSubmit)="save()">
                <app-business-fields [fields]="form" [categories]="categories()" prefix="edit-business" />

                <div class="actions">
                  <button class="btn" type="submit" [disabled]="busy() || !canSave()">
                    {{ busy() ? 'Saving…' : 'Save changes' }}
                  </button>
                  <button class="btn secondary" type="button" (click)="closePopup()">Cancel</button>
                </div>
              </form>
            </div>
          </div>
        }

        <!-- Add or edit a product ----------------------------------------- -->
        @if (popup() === 'product') {
          <div class="modal-backdrop" (click)="closePopup()">
            <div class="card modal-card modal-dialog" role="dialog" aria-modal="true" aria-labelledby="product-popup-title"
              (click)="$event.stopPropagation()" (keydown.escape)="closePopup()">
              <h2 id="product-popup-title">{{ editingProduct() ? 'Edit ' + editingProduct()!.name : 'Add a product' }}</h2>

              @if (error(); as message) {
                <p class="notice error" role="alert">{{ message }}</p>
              }

              <form (ngSubmit)="saveProduct(found)">
                <div class="form-grid">
                  <div class="field">
                    <label for="product-name">Name</label>
                    <input class="input" id="product-name" name="productName" maxlength="200" [(ngModel)]="productForm.name" />
                  </div>

                  <div class="field">
                    <label for="product-price">Price (₹)</label>
                    <input class="input" id="product-price" name="productPrice" inputmode="decimal"
                      [(ngModel)]="productPriceInput" placeholder="450" />
                  </div>

                  <div class="field span-all">
                    <label for="product-description">Description</label>
                    <textarea class="input" id="product-description" name="productDescription" rows="2" maxlength="500"
                      [(ngModel)]="productForm.description"></textarea>
                  </div>

                  <div class="field span-all">
                    <label for="product-photo">
                      {{ editingProduct()?.photoUrl ? 'Change the image (optional)' : 'Image (optional)' }}
                    </label>
                    @if (editingProduct()?.photoUrl; as path) {
                      <img class="product-thumb" [scAuthedSrc]="path" alt="The product's current image" />
                    }
                    <input id="product-photo" class="input" type="file" name="productPhoto"
                      accept="image/jpeg,image/png,image/webp" (change)="chooseProductPhoto($event)" />
                    <p class="small">JPEG, PNG or WebP, 2 MB or smaller.</p>
                  </div>
                </div>

                <div class="actions">
                  <button class="btn" type="submit" [disabled]="busy()">
                    {{ editingProduct() ? 'Save product' : 'Add product' }}
                  </button>
                  @if (editingProduct()?.photoUrl) {
                    <button class="btn secondary" type="button" [disabled]="busy()" (click)="removeProductPhoto(found)">
                      Remove the image
                    </button>
                  }
                  <button class="btn secondary" type="button" (click)="closePopup()">Cancel</button>
                </div>
              </form>
            </div>
          </div>
        }
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
  readonly loadError = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);
  readonly busy = signal(false);

  readonly categories = signal<readonly LookupItem[]>([]);
  readonly cities = signal<readonly City[]>([]);
  readonly localities = signal<readonly Locality[]>([]);

  readonly tabs: readonly { value: BusinessTab; label: string }[] = [
    { value: 'details', label: 'Details' },
    { value: 'products', label: 'Products' },
  ];

  readonly tab = signal<BusinessTab>('details');

  /** Which form is open over the page, if any. */
  readonly popup = signal<'details' | 'product' | null>(null);

  /** The product the product popup is editing, or null when it adds one. */
  readonly editingProductId = signal<string | null>(null);

  readonly editingProduct = computed(() => {
    const id = this.editingProductId();

    return id === null ? null : (this.business()?.products.find((p) => p.id === id) ?? null);
  });

  readonly confirmingStatus = signal(false);

  form: BusinessFields = blankFields();

  productForm: ProductFields = { name: '', description: null, priceInPaise: 0 };
  productPriceInput = '';
  productPhoto: File | null = null;

  ngOnInit(): void {
    this.masterData
      .listBusinessCategories()
      .subscribe({ next: (found) => this.categories.set(found), error: () => {} });
    this.masterData.listCities().subscribe({ next: (found) => this.cities.set(found), error: () => {} });
    this.masterData.listLocalities().subscribe({ next: (found) => this.localities.set(found), error: () => {} });

    this.load();
  }

  private businessId(): string | null {
    return this.route.snapshot.paramMap.get('id');
  }

  initials(name: string): string {
    return initialsOf(name);
  }

  categoryLabel(categoryId: string): string | null {
    return this.categories().find((c) => c.id === categoryId)?.label ?? null;
  }

  place(business: Business): string | null {
    return placeOf(business, this.cities(), this.localities());
  }

  formatPrice(paise: number): string {
    return formatRupees(paise);
  }

  load(): void {
    const id = this.businessId();

    if (id === null) {
      this.loading.set(false);
      this.loadError.set('That link does not name a business.');
      return;
    }

    this.api.get(id).subscribe({
      next: (found) => {
        this.business.set(found);
        this.loading.set(false);
        this.loadError.set(null);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(failure));
      },
    });
  }

  closePopup(): void {
    this.popup.set(null);
    this.error.set(null);
  }

  // ---- Details -------------------------------------------------------------

  openDetails(found: Business): void {
    this.form = {
      name: found.name,
      categoryId: found.categoryId,
      cityId: found.cityId,
      localityId: found.localityId,
      addressLine: found.addressLine,
      description: found.description,
      contactPhone: found.contactPhone,
      contactEmail: found.contactEmail,
    };
    this.openPopup('details');
  }

  canSave(): boolean {
    return this.form.name.trim().length > 0 && this.form.categoryId.length > 0;
  }

  save(): void {
    const id = this.businessId();

    if (id === null || !this.canSave()) {
      return;
    }

    this.run(this.api.update(id, this.form), 'Details saved.');
  }

  uploadPhoto(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    const id = this.businessId();

    if (!file || id === null) {
      return;
    }

    this.run(this.api.uploadPhoto(id, file), 'Photo saved.', () => (input.value = ''));
  }

  removePhoto(): void {
    const id = this.businessId();

    if (id !== null) {
      this.run(this.api.removePhoto(id), 'Photo removed.');
    }
  }

  setActive(active: boolean): void {
    const id = this.businessId();

    if (id === null) {
      return;
    }

    this.run(this.api.setStatus(id, active), active ? 'Back in the directory.' : 'Taken out of the directory.', () =>
      this.confirmingStatus.set(false),
    );
  }

  // ---- Products ------------------------------------------------------------

  openProduct(product: Product | null): void {
    this.editingProductId.set(product?.id ?? null);
    this.productForm = product
      ? { name: product.name, description: product.description, priceInPaise: product.priceInPaise }
      : { name: '', description: null, priceInPaise: 0 };
    this.productPriceInput = product ? toInputValue(product.priceInPaise) : '';
    this.productPhoto = null;
    this.openPopup('product');
  }

  chooseProductPhoto(event: Event): void {
    this.productPhoto = (event.target as HTMLInputElement).files?.[0] ?? null;
  }

  saveProduct(found: Business): void {
    const priceInPaise = parseRupees(this.productPriceInput);

    if (this.productForm.name.trim().length === 0 || priceInPaise === null || priceInPaise <= 0) {
      this.error.set('Enter a name and a price greater than zero.');
      return;
    }

    const fields: ProductFields = { ...this.productForm, priceInPaise };
    const editingId = this.editingProductId();
    const photo = this.productPhoto;
    const known = new Set(found.products.map((p) => p.id));
    const name = fields.name.trim();

    this.busy.set(true);
    this.error.set(null);

    const saved = editingId === null
      ? this.api.addProduct(found.id, fields)
      : this.api.updateProduct(found.id, editingId, fields);

    saved.subscribe({
      next: (updated) => {
        // A new product is the one the business did not have a moment ago.
        const productId = editingId ?? updated.products.find((p) => !known.has(p.id))?.id ?? null;

        if (photo === null || productId === null) {
          this.succeed(updated, editingId === null ? `${name} added.` : `${name} saved.`);
          return;
        }

        this.api.uploadProductPhoto(found.id, productId, photo).subscribe({
          next: (withPhoto) => this.succeed(withPhoto, editingId === null ? `${name} added, with its image.` : `${name} saved.`),
          error: (failure: unknown) => {
            // The product itself is saved; only its picture is not.
            this.business.set(updated);
            this.busy.set(false);
            this.editingProductId.set(productId);
            this.error.set(`${name} is saved, but its image was not: ${describeError(failure)}`);
          },
        });
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  removeProductPhoto(found: Business): void {
    const product = this.editingProduct();

    if (product !== null) {
      this.run(this.api.removeProductPhoto(found.id, product.id), `${product.name}'s image removed.`);
    }
  }

  toggleAvailability(product: Product): void {
    const found = this.business();

    if (found === null) {
      return;
    }

    this.run(
      this.api.setProductAvailability(found.id, product.id, !product.isAvailable),
      product.isAvailable ? `${product.name} is off sale.` : `${product.name} is back on sale.`,
    );
  }

  // ---- Plumbing ------------------------------------------------------------

  private openPopup(which: 'details' | 'product'): void {
    this.error.set(null);
    this.done.set(null);
    this.popup.set(which);
  }

  /** Sends one change; on success shows the business as the service answered it and closes any popup. */
  private run(work: Observable<Business>, message: string, after?: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: (updated) => {
        this.succeed(updated, message);
        after?.();
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  private succeed(updated: Business, message: string): void {
    this.business.set(updated);
    this.busy.set(false);
    this.popup.set(null);
    this.done.set(message);
  }
}

type BusinessTab = 'details' | 'products';

function blankFields(): BusinessFields {
  return {
    name: '',
    categoryId: '',
    cityId: null,
    localityId: null,
    addressLine: null,
    description: null,
    contactPhone: null,
    contactEmail: null,
  };
}
