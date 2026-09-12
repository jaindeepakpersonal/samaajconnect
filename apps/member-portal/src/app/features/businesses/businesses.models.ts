/**
 * Wire shapes for businesses-service, mirroring `BusinessResponses.cs` and
 * `OrderResponses.cs`.
 */

/** `OrderStatus` in the domain. */
export type OrderStatus = 'Pending' | 'Accepted' | 'Declined' | 'Fulfilled' | 'Cancelled';

export interface Product {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
  readonly priceInPaise: number;
  readonly isAvailable: boolean;
}

/**
 * A business as either its owner or a directory viewer sees it. `products`
 * carries every product to the owner and only the available ones to anyone
 * else - the service decides which, not the client.
 */
export interface Business {
  readonly id: string;
  readonly ownerMemberId: string;
  readonly name: string;
  readonly categoryId: string;
  readonly cityId: string | null;
  readonly localityId: string | null;
  readonly description: string | null;
  readonly contactPhone: string | null;
  readonly contactEmail: string | null;
  readonly isActive: boolean;
  readonly products: readonly Product[];
  readonly createdAt: string;
  readonly updatedAt: string | null;
}

/** Every field the create and edit forms submit. The same shape both ways. */
export interface BusinessFields {
  name: string;
  categoryId: string;
  cityId: string | null;
  localityId: string | null;
  description: string | null;
  contactPhone: string | null;
  contactEmail: string | null;
}

export interface ProductFields {
  name: string;
  description: string | null;
  priceInPaise: number;
}

export interface DirectoryFilters {
  categoryId: string;
  cityId: string;
  localityId: string;
  term: string;
}

export interface OrderItemLine {
  readonly productId: string;
  readonly productName: string;
  readonly unitPriceInPaise: number;
  readonly quantity: number;
}

/**
 * An order as either the buyer or the business owner sees it. `businessName`
 * is resolved by the service at read time, not stored on the order itself -
 * an owner running several businesses needs to tell Received rows apart.
 */
export interface Order {
  readonly id: string;
  readonly businessId: string;
  readonly businessName: string;
  readonly buyerMemberId: string;
  readonly status: OrderStatus;
  readonly items: readonly OrderItemLine[];
  readonly totalAmountInPaise: number;
  readonly note: string | null;
  readonly placedAt: string;
  readonly respondedAt: string | null;
  readonly closedAt: string | null;
}

export interface PlaceOrderItem {
  productId: string;
  quantity: number;
}
