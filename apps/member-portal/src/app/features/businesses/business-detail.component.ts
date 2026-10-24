import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService, AuthedImageDirective, describeError, formatRupees } from '@samaajconnect/shared';
import { MasterDataApi } from '../members/master-data.api';
import { City, Locality, LookupItem } from '../members/members.models';
import { MemberLookup, initialsOf } from '../members/member-lookup';
import { placeOf } from './business-place';
import { BusinessesApi } from './businesses.api';
import { Business, PlaceOrderItem, Product } from './businesses.models';

/**
 * A business's public page, `/businesses/:id` - the shop front: its photo,
 * where it is, who runs it and how to reach them, then its products and an
 * order.
 *
 * **The products read like an online shop (2026-10-24, the user's request)** -
 * a grid of tiles with each product's image, name and price, an Add button that
 * becomes a − n + stepper, and a "Your order" panel beside them (below them on
 * a phone) listing what is in it with the total and Place order. The total is
 * still only a display: businesses-service prices the order itself from each
 * product at the moment it is placed, and the client's figure is never sent.
 *
 * Hidden for the owner viewing their own listing, the same reasoning the
 * service itself refuses ordering from your own business.
 */
@Component({
  selector: 'app-business-detail',
  imports: [FormsModule, RouterLink, AuthedImageDirective],
  styleUrl: './businesses.css',
  template: `
    <div class="businesses-page">
      <a class="back" routerLink="/businesses">‹ Back to directory</a>

      @if (loading()) {
        <p role="status">Loading business…</p>
      } @else if (error(); as message) {
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

            @if (categoryLabel(found.categoryId); as category) {
              <span class="pill">{{ category }}</span>
            }

            @if (whereIs(found); as where) {
              <p class="biz-place">{{ where }}</p>
            }

            @if (found.description; as description) {
              <p class="group-body">{{ description }}</p>
            }

            <div class="biz-contact">
              <div class="owner-chip">
                @let owner = owners.member(found.ownerMemberId);
                @if (owner?.photoUrl; as path) {
                  <img class="owner-avatar" [scAuthedSrc]="path" alt="" />
                } @else {
                  <span class="owner-avatar initials" aria-hidden="true">{{ owner ? initials(owner.fullName) : '' }}</span>
                }
                <span>
                  Run by
                  @if (owner) {
                    <a [routerLink]="['/members', owner.id]">{{ owner.fullName }}</a>
                  } @else if (owner === null) {
                    a member
                  } @else {
                    …
                  }
                </span>
              </div>

              @if (found.contactPhone; as phone) {
                <a class="btn small secondary" [href]="'tel:' + phone">Call {{ phone }}</a>
              }
              @if (found.contactEmail; as email) {
                <a class="btn small secondary" [href]="'mailto:' + email">Email {{ email }}</a>
              }
            </div>
          </div>
        </header>

        <div class="shop" [class.no-cart]="isOwner()">
          <section aria-labelledby="products-heading">
            <h2 id="products-heading" class="section-heading">Products</h2>

            @if (found.products.length === 0) {
              <p class="notice info" role="status">Nothing listed yet.</p>
            } @else {
              <ul class="product-grid" aria-label="Products">
                @for (product of found.products; track product.id) {
                  <li class="product-card">
                    @if (product.photoUrl; as path) {
                      <img class="product-image" [scAuthedSrc]="path" alt="" />
                    } @else {
                      <span class="product-image placeholder" aria-hidden="true">{{ initials(product.name) }}</span>
                    }

                    <div class="product-body">
                      <h3 class="product-name">{{ product.name }}</h3>
                      @if (product.description; as description) {
                        <p class="product-description">{{ description }}</p>
                      }
                      <p class="price">{{ formatPrice(product.priceInPaise) }}</p>

                      @if (!isOwner()) {
                        @if (quantityOf(product) === 0) {
                          <button class="btn small add-btn" type="button" (click)="add(product)">
                            Add<span class="sr-only"> {{ product.name }} to your order</span>
                          </button>
                        } @else {
                          <div class="stepper" role="group" [attr.aria-label]="'Quantity of ' + product.name">
                            <button class="btn small secondary" type="button" (click)="remove(product)">
                              <span aria-hidden="true">−</span><span class="sr-only">One fewer {{ product.name }}</span>
                            </button>
                            <span class="stepper-count" aria-live="polite">{{ quantityOf(product) }}</span>
                            <button class="btn small secondary" type="button" (click)="add(product)">
                              <span aria-hidden="true">+</span><span class="sr-only">One more {{ product.name }}</span>
                            </button>
                          </div>
                        }
                      }
                    </div>
                  </li>
                }
              </ul>
            }
          </section>

          @if (isOwner()) {
            <p class="notice info" role="status">
              This is your own business - you cannot order from it.
              <a [routerLink]="['/businesses/mine', found.id]">Manage it</a>
            </p>
          } @else {
            <aside class="card cart" aria-labelledby="cart-heading">
              <h2 id="cart-heading">Your order</h2>

              @if (orderPlaced()) {
                <p class="notice info" role="status">Your order has been sent to the business owner.</p>
                <a class="btn small secondary" routerLink="/businesses/orders">See your orders</a>
              } @else {
                @if (lines().length === 0) {
                  <p class="small">Nothing yet. Add products to order them.</p>
                } @else {
                  <ul class="cart-lines">
                    @for (line of lines(); track line.product.id) {
                      <li>
                        <span>{{ line.product.name }} × {{ line.quantity }}</span>
                        <span>{{ formatPrice(line.product.priceInPaise * line.quantity) }}</span>
                      </li>
                    }
                  </ul>
                  <p class="cart-total"><span>Total</span> <b>{{ formatPrice(runningTotal()) }}</b></p>
                }

                <label for="note">Note to the owner (optional)</label>
                <textarea class="input" id="note" name="note" rows="3" maxlength="500" [(ngModel)]="note"></textarea>

                @if (orderError(); as message) {
                  <p class="notice error" role="alert">{{ message }}</p>
                }

                <button class="btn cart-place" type="button" [disabled]="placing() || runningTotal() === 0" (click)="place(found.id)">
                  {{ placing() ? 'Sending…' : 'Place order' }}
                </button>
                <p class="small">The owner prices and confirms the order. Nothing is paid here.</p>
              }
            </aside>
          }
        </div>
      }
    </div>
  `,
})
export class BusinessDetailComponent implements OnInit {
  private readonly api = inject(BusinessesApi);
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);
  private readonly masterData = inject(MasterDataApi);

  readonly owners = inject(MemberLookup);

  private readonly me = computed(() => this.auth.user()?.userId ?? null);

  readonly business = signal<Business | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly categories = signal<readonly LookupItem[]>([]);
  readonly cities = signal<readonly City[]>([]);
  readonly localities = signal<readonly Locality[]>([]);

  readonly placing = signal(false);
  readonly orderError = signal<string | null>(null);
  readonly orderPlaced = signal(false);

  /** How many of each product are in the order. */
  readonly quantities = signal<Readonly<Record<string, number>>>({});

  note = '';

  readonly isOwner = computed(() => {
    const found = this.business();
    const memberId = this.me();

    return found !== null && memberId !== null && found.ownerMemberId === memberId;
  });

  /** What is in the order, in the order the products are listed. */
  readonly lines = computed(() =>
    (this.business()?.products ?? [])
      .map((product) => ({ product, quantity: this.quantities()[product.id] ?? 0 }))
      .filter((line) => line.quantity > 0),
  );

  readonly runningTotal = computed(() =>
    this.lines().reduce((total, line) => total + line.product.priceInPaise * line.quantity, 0),
  );

  ngOnInit(): void {
    this.masterData
      .listBusinessCategories()
      .subscribe({ next: (found) => this.categories.set(found), error: () => {} });
    this.masterData.listCities().subscribe({ next: (found) => this.cities.set(found), error: () => {} });
    this.masterData.listLocalities().subscribe({ next: (found) => this.localities.set(found), error: () => {} });

    this.load();
  }

  initials(name: string): string {
    return initialsOf(name);
  }

  categoryLabel(categoryId: string): string | null {
    return this.categories().find((c) => c.id === categoryId)?.label ?? null;
  }

  whereIs(business: Business): string | null {
    return placeOf(business, this.cities(), this.localities());
  }

  load(): void {
    const id = this.route.snapshot.paramMap.get('id');

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
        this.quantities.set({});
        this.owners.resolve([found.ownerMemberId]);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  quantityOf(product: Product): number {
    return this.quantities()[product.id] ?? 0;
  }

  add(product: Product): void {
    this.setQuantity(product.id, this.quantityOf(product) + 1);
  }

  remove(product: Product): void {
    this.setQuantity(product.id, this.quantityOf(product) - 1);
  }

  setQuantity(productId: string, value: number): void {
    this.quantities.update((current) => ({ ...current, [productId]: Math.max(0, Math.floor(value) || 0) }));
  }

  formatPrice(paise: number): string {
    return formatRupees(paise);
  }

  place(businessId: string): void {
    // Products left at zero are not sent at all.
    const items: PlaceOrderItem[] = this.lines().map((line) => ({ productId: line.product.id, quantity: line.quantity }));

    if (items.length === 0) {
      return;
    }

    this.placing.set(true);
    this.orderError.set(null);

    const trimmedNote = this.note.trim();

    this.api.placeOrder(businessId, items, trimmedNote.length > 0 ? trimmedNote : null).subscribe({
      next: () => {
        this.placing.set(false);
        this.orderPlaced.set(true);
      },
      error: (failure: unknown) => {
        this.placing.set(false);
        this.orderError.set(describeError(failure));
      },
    });
  }
}
