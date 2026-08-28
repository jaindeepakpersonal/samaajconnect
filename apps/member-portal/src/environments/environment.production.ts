import { ApiConfig } from '@samaajconnect/shared';

/**
 * Production. The portal and the gateway sit on the same origin, so relative
 * URLs are correct and there is nothing to configure. The platform runs on one
 * domain: which Samaaj a member belongs to travels in their token, not in the
 * host (root CLAUDE.md section 6).
 */
export const environment: { production: boolean; api: ApiConfig } = {
  production: true,
  api: { gatewayUrl: '' },
};
