import { ApiConfig } from '@samaajconnect/shared';

/**
 * Development. Requests go to the same origin and the Angular dev server
 * proxies /v1 to the gateway, adding the Host header that tells the gateway
 * which Samaaj the browser is standing on (see proxy.conf.json).
 */
export const environment: { production: boolean; api: ApiConfig } = {
  production: false,
  api: { gatewayUrl: '' },
};
