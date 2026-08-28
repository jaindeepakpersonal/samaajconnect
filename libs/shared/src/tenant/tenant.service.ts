import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, inject } from '@angular/core';
import { API_CONFIG } from '../api/api-config';

/**
 * Knows which Samaaj the browser is currently on.
 *
 * In a real deployment the answer is the subdomain, and the gateway derives the
 * same thing from the Host header. This exists so the UI can name the Samaaj
 * it is showing, and so local development against `localhost` still works.
 */
@Injectable({ providedIn: 'root' })
export class TenantService {
  private readonly config = inject(API_CONFIG);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly document = inject(DOCUMENT, { optional: true });

  /** The Samaaj slug for this browsing session, or null on the apex host. */
  get slug(): string | null {
    if (this.config.tenantSlugOverride) {
      return this.config.tenantSlugOverride;
    }

    if (!isPlatformBrowser(this.platformId) || !this.document) {
      // Server-side rendering has no window. The gateway has already resolved
      // the Samaaj from the Host header before a request reaches a service, so
      // there is nothing to guess here.
      return null;
    }

    return TenantService.slugFromHost(this.document.location.hostname);
  }

  /**
   * `mahavir-samaj.samaajconnect.com` -> `mahavir-samaj`.
   *
   * Mirrors the gateway's HostSlugExtractor. The two must agree: if they drift,
   * the UI names one Samaaj while the API serves another and nothing fails
   * loudly. The spec beside this file covers the same cases as the gateway's.
   */
  static slugFromHost(hostname: string | null | undefined): string | null {
    if (!hostname) {
      return null;
    }

    const host = hostname.split(':')[0].trim().replace(/\.$/, '').toLowerCase();

    if (!host || host === 'localhost' || /^[\d.]+$/.test(host) || host.includes('[')) {
      return null;
    }

    const labels = host.split('.').filter(Boolean);

    return labels.length < 2 ? null : labels[0];
  }
}
