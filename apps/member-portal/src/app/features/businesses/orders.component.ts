import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { describeError, formatRupees } from '@samaajconnect/shared';
import { BusinessesApi } from './businesses.api';
import { Order } from './businesses.models';

type OrderTab = 'placed' | 'received';

/**
 * Orders, `/businesses/orders` - Placed/Received tabs, the same
 * `role="tablist"` pattern Matrimony's own Proposals screen already uses.
 * The Received tab is only useful to a member who owns at least one
 * business, but is always rendered - an empty list says so plainly rather
 * than hiding the tab and leaving an owner wondering where it went.
 */
@Component({
  selector: 'app-businesses-orders',
  imports: [RouterLink],
  styleUrl: './businesses.css',
  template: `
    <div class="businesses-page">
      <a class="back" routerLink="/businesses">‹ Back to directory</a>

      <header class="page-head">
        <h1 class="page-title">Orders</h1>
      </header>

      <div class="tabs" role="tablist" aria-label="Orders">
        <button
          type="button"
          role="tab"
          [class.on]="activeTab() === 'placed'"
          [attr.aria-selected]="activeTab() === 'placed'"
          (click)="activeTab.set('placed')"
        >
          Placed
        </button>
        <button
          type="button"
          role="tab"
          [class.on]="activeTab() === 'received'"
          [attr.aria-selected]="activeTab() === 'received'"
          (click)="activeTab.set('received')"
        >
          Received
        </button>
      </div>

      @if (loading()) {
        <p role="status">Loading…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else {
        @if (actionError(); as message) {
          <p class="notice error" role="alert">{{ message }}</p>
        }

        @if (activeTab() === 'placed') {
          @if (placed().length === 0) {
            <p class="notice info" role="status">You have not placed any orders.</p>
          } @else {
            <div class="queue">
              @for (order of placed(); track order.id) {
                <div class="card">
                  <p>
                    <b>{{ order.businessName }}</b>
                    <span class="pill" [class]="statusClass(order)">{{ order.status }}</span>
                  </p>
                  <p class="small">
                    @for (item of order.items; track item.productId) {
                      {{ item.quantity }} × {{ item.productName }}@if (!$last) {, }
                    }
                  </p>
                  <p class="small">Total: {{ formatPrice(order.totalAmountInPaise) }}</p>
                  @if (order.note; as note) {
                    <p class="group-body">{{ note }}</p>
                  }
                  @if (order.status === 'Pending') {
                    <div class="actions">
                      <button class="btn small secondary" type="button" [disabled]="busy()" (click)="cancel(order.id)">
                        Cancel
                      </button>
                    </div>
                  }
                </div>
              }
            </div>
          }
        } @else {
          @if (received().length === 0) {
            <p class="notice info" role="status">No orders against your businesses yet.</p>
          } @else {
            <div class="queue">
              @for (order of received(); track order.id) {
                <div class="card">
                  <p>
                    <b>{{ order.businessName }}</b>
                    <span class="pill" [class]="statusClass(order)">{{ order.status }}</span>
                  </p>
                  <p class="small">
                    @for (item of order.items; track item.productId) {
                      {{ item.quantity }} × {{ item.productName }}@if (!$last) {, }
                    }
                  </p>
                  <p class="small">Total: {{ formatPrice(order.totalAmountInPaise) }}</p>
                  @if (order.note; as note) {
                    <p class="group-body">{{ note }}</p>
                  }
                  @if (order.status === 'Pending') {
                    <div class="actions">
                      <button class="btn small" type="button" [disabled]="busy()" (click)="respond(order.id, true)">
                        Accept
                      </button>
                      <button
                        class="btn small secondary"
                        type="button"
                        [disabled]="busy()"
                        (click)="respond(order.id, false)"
                      >
                        Decline
                      </button>
                    </div>
                  }
                  @if (order.status === 'Accepted') {
                    <div class="actions">
                      <button class="btn small" type="button" [disabled]="busy()" (click)="fulfill(order.id)">
                        Mark fulfilled
                      </button>
                      <button class="btn small secondary" type="button" [disabled]="busy()" (click)="cancel(order.id)">
                        Cancel
                      </button>
                    </div>
                  }
                </div>
              }
            </div>
          }
        }
      }
    </div>
  `,
})
export class BusinessesOrdersComponent implements OnInit {
  private readonly api = inject(BusinessesApi);

  readonly activeTab = signal<OrderTab>('placed');

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly placed = signal<readonly Order[]>([]);
  readonly received = signal<readonly Order[]>([]);

  readonly busy = signal(false);
  readonly actionError = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.myOrders().subscribe({
      next: (found) => {
        this.placed.set(found);
        this.loadReceived();
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  private loadReceived(): void {
    this.api.receivedOrders().subscribe({
      next: (found) => {
        this.received.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  respond(orderId: string, accept: boolean): void {
    this.busy.set(true);
    this.actionError.set(null);

    this.api.respondToOrder(orderId, accept).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(failure));
      },
    });
  }

  fulfill(orderId: string): void {
    this.busy.set(true);
    this.actionError.set(null);

    this.api.fulfillOrder(orderId).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(failure));
      },
    });
  }

  cancel(orderId: string): void {
    this.busy.set(true);
    this.actionError.set(null);

    this.api.cancelOrder(orderId).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(failure));
      },
    });
  }

  formatPrice(paise: number): string {
    return formatRupees(paise);
  }

  statusClass(order: Order): string {
    switch (order.status) {
      case 'Accepted':
      case 'Fulfilled':
        return 'ok';
      case 'Pending':
        return 'warn';
      case 'Declined':
      case 'Cancelled':
        return 'danger';
    }
  }
}
