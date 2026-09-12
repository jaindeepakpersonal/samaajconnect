import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService, describeError, formatRupees } from '@samaajconnect/shared';
import { BusinessesApi } from './businesses.api';
import { Business, PlaceOrderItem } from './businesses.models';

/**
 * A business's public detail, `/businesses/:id` - full info, its available
 * products, and "Place an order". Hidden for the owner viewing their own
 * listing, the same reasoning the service itself refuses ordering from your
 * own business.
 */
@Component({
  selector: 'app-business-detail',
  imports: [FormsModule, RouterLink],
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
        <h1 class="page-title">{{ found.name }}</h1>
        @if (found.description; as description) {
          <p class="subtitle">{{ description }}</p>
        }

        <div class="card">
          <h2>Contact</h2>
          <dl class="details">
            @if (found.contactPhone; as phone) {
              <dt>Phone</dt>
              <dd>{{ phone }}</dd>
            }
            @if (found.contactEmail; as email) {
              <dt>Email</dt>
              <dd>{{ email }}</dd>
            }
          </dl>
        </div>

        <div class="card">
          <h2>Products</h2>

          @if (found.products.length === 0) {
            <p class="notice info" role="status">Nothing listed yet.</p>
          } @else {
            @for (product of found.products; track product.id) {
              <div class="product-row">
                <div>
                  <b>{{ product.name }}</b>
                  @if (product.description; as description) {
                    <div class="small">{{ description }}</div>
                  }
                </div>
                <div>
                  {{ formatPrice(product.priceInPaise) }}
                  @if (isOwner()) {
                    <input
                      class="input"
                      type="number"
                      min="0"
                      style="width: 70px"
                      [attr.aria-label]="'Quantity of ' + product.name"
                      disabled
                    />
                  } @else {
                    <input
                      class="input"
                      type="number"
                      min="0"
                      style="width: 70px"
                      [attr.aria-label]="'Quantity of ' + product.name"
                      [ngModel]="quantities[product.id] ?? 0"
                      (ngModelChange)="setQuantity(product.id, $event)"
                    />
                  }
                </div>
              </div>
            }
          }
        </div>

        @if (isOwner()) {
          <p class="notice info" role="status">This is your own business - you cannot order from it.</p>
        } @else {
          @if (orderError(); as message) {
            <p class="notice error" role="alert">{{ message }}</p>
          }
          @if (orderPlaced()) {
            <p class="notice info" role="status">Your order has been sent to the business owner.</p>
          } @else {
            <div class="card">
              <h2>Place an order</h2>

              <label for="note">Note (optional)</label>
              <textarea class="input" id="note" name="note" rows="3" maxlength="500" [(ngModel)]="note"></textarea>

              <p class="small">Total: {{ formatPrice(runningTotal()) }}</p>

              <div class="actions">
                <button class="btn" type="button" [disabled]="placing() || runningTotal() === 0" (click)="place(found.id)">
                  {{ placing() ? 'Sending…' : 'Place order' }}
                </button>
              </div>
            </div>
          }
        }
      }
    </div>
  `,
})
export class BusinessDetailComponent implements OnInit {
  private readonly api = inject(BusinessesApi);
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);

  private readonly me = computed(() => this.auth.user()?.userId ?? null);

  readonly business = signal<Business | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly placing = signal(false);
  readonly orderError = signal<string | null>(null);
  readonly orderPlaced = signal(false);

  quantities: Record<string, number> = {};
  note = '';

  readonly isOwner = computed(() => {
    const found = this.business();
    const memberId = this.me();

    return found !== null && memberId !== null && found.ownerMemberId === memberId;
  });

  ngOnInit(): void {
    this.load();
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
        this.quantities = {};
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  setQuantity(productId: string, value: number): void {
    this.quantities = { ...this.quantities, [productId]: Math.max(0, Math.floor(value) || 0) };
  }

  runningTotal(): number {
    const found = this.business();

    if (found === null) {
      return 0;
    }

    return found.products.reduce((total, product) => {
      const quantity = this.quantities[product.id] ?? 0;

      return total + product.priceInPaise * quantity;
    }, 0);
  }

  formatPrice(paise: number): string {
    return formatRupees(paise);
  }

  place(businessId: string): void {
    const items: PlaceOrderItem[] = Object.entries(this.quantities)
      .filter(([, quantity]) => quantity > 0)
      .map(([productId, quantity]) => ({ productId, quantity }));

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
