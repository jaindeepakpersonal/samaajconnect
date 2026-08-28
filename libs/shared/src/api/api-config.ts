import { InjectionToken } from '@angular/core';

/**
 * Where the gateway lives. Injected rather than imported from an environment
 * file so each app - and each test - can point somewhere different without a
 * rebuild.
 */
export interface ApiConfig {
  /** Base URL of the YARP gateway. Empty string means same-origin. */
  readonly gatewayUrl: string;
}

export const API_CONFIG = new InjectionToken<ApiConfig>('API_CONFIG');
