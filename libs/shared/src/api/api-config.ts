import { InjectionToken } from '@angular/core';

/**
 * Where the gateway lives. Injected rather than imported from an environment
 * file so each app - and each test - can point somewhere different without a
 * rebuild.
 */
export interface ApiConfig {
  /** Base URL of the YARP gateway. Empty string means same-origin. */
  readonly gatewayUrl: string;

  /**
   * Samaaj slug to send explicitly, for local development where the browser is
   * on `localhost` rather than `{slug}.samaajconnect.com`. In a real
   * deployment this stays undefined and the gateway derives the Samaaj from
   * the host it was reached on (ARCHITECTURE.md section 7).
   */
  readonly tenantSlugOverride?: string;
}

export const API_CONFIG = new InjectionToken<ApiConfig>('API_CONFIG');
