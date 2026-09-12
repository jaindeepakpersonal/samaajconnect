import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  Business,
  BusinessFields,
  DirectoryFilters,
  Order,
  PlaceOrderItem,
  ProductFields,
} from './businesses.models';

/**
 * Every call this app makes to businesses-service.
 *
 * Module-gated on `businesses`, off by default. Every write is self-scoped
 * (the same role list `UpdateProfileCommand`/Matrimony's own commands use);
 * every read is `Members.Read`, already broadly granted.
 */
@Injectable({ providedIn: 'root' })
export class BusinessesApi {
  private readonly http = inject(HttpClient);

  mine(): Observable<Business[]> {
    return this.http.get<Business[]>('/v1/businesses/mine');
  }

  create(fields: BusinessFields): Observable<Business> {
    return this.http.post<Business>('/v1/businesses/mine', fields);
  }

  update(businessId: string, fields: BusinessFields): Observable<Business> {
    return this.http.patch<Business>(`/v1/businesses/mine/${businessId}`, fields);
  }

  setStatus(businessId: string, active: boolean): Observable<Business> {
    return this.http.post<Business>(`/v1/businesses/mine/${businessId}/status`, { active });
  }

  addProduct(businessId: string, fields: ProductFields): Observable<Business> {
    return this.http.post<Business>(`/v1/businesses/mine/${businessId}/products`, fields);
  }

  updateProduct(businessId: string, productId: string, fields: ProductFields): Observable<Business> {
    return this.http.patch<Business>(`/v1/businesses/mine/${businessId}/products/${productId}`, fields);
  }

  setProductAvailability(businessId: string, productId: string, available: boolean): Observable<Business> {
    return this.http.post<Business>(
      `/v1/businesses/mine/${businessId}/products/${productId}/availability`,
      { available },
    );
  }

  search(filters: DirectoryFilters): Observable<Business[]> {
    let params = new HttpParams();

    if (filters.categoryId) {
      params = params.set('categoryId', filters.categoryId);
    }
    if (filters.cityId) {
      params = params.set('cityId', filters.cityId);
    }
    if (filters.localityId) {
      params = params.set('localityId', filters.localityId);
    }
    if (filters.term) {
      params = params.set('term', filters.term);
    }

    return this.http.get<Business[]>('/v1/businesses', { params });
  }

  get(businessId: string): Observable<Business> {
    return this.http.get<Business>(`/v1/businesses/${businessId}`);
  }

  placeOrder(businessId: string, items: PlaceOrderItem[], note: string | null): Observable<Order> {
    return this.http.post<Order>('/v1/businesses/orders', { businessId, items, note });
  }

  myOrders(): Observable<Order[]> {
    return this.http.get<Order[]>('/v1/businesses/orders/mine');
  }

  receivedOrders(): Observable<Order[]> {
    return this.http.get<Order[]>('/v1/businesses/orders/received');
  }

  /** The business owner only. */
  respondToOrder(orderId: string, accept: boolean): Observable<Order> {
    return this.http.post<Order>(`/v1/businesses/orders/${orderId}/respond`, { accept });
  }

  /** The business owner only, once accepted. */
  fulfillOrder(orderId: string): Observable<Order> {
    return this.http.post<Order>(`/v1/businesses/orders/${orderId}/fulfill`, {});
  }

  /** The buyer while pending, or the owner once accepted. */
  cancelOrder(orderId: string): Observable<Order> {
    return this.http.post<Order>(`/v1/businesses/orders/${orderId}/cancel`, {});
  }
}
