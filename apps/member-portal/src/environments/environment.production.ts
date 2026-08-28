import { ApiConfig } from '@samaajconnect/shared';

/**
 * Production. The portal is served from the Samaaj's own subdomain and the
 * gateway sits on the same origin, so relative URLs are correct and the Host
 * header the browser sends already names the Samaaj.
 */
export const environment: { production: boolean; api: ApiConfig } = {
  production: true,
  api: { gatewayUrl: '' },
};
