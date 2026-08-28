import { ApiConfig } from '@samaajconnect/shared';

/**
 * Production. The panel and the gateway sit on the same origin, so relative
 * URLs are correct and there is nothing to configure.
 */
export const environment: { production: boolean; api: ApiConfig } = {
  production: true,
  api: { gatewayUrl: '' },
};
