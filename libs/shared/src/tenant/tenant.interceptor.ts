import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { API_CONFIG } from '../api/api-config';

/**
 * Rewrites relative API URLs to the gateway.
 *
 * Note what this deliberately does *not* do: attach a tenant. The platform runs
 * on one domain and a member's Samaaj travels in their token, so there is
 * nothing for the client to say about it - and the gateway strips every inbound
 * tenant header anyway, because that is the control stopping a client from
 * choosing its own Samaaj (root CLAUDE.md section 6).
 */
export const tenantInterceptor: HttpInterceptorFn = (request, next) => {
  const config = inject(API_CONFIG);

  if (/^https?:\/\//i.test(request.url)) {
    return next(request);
  }

  return next(request.clone({ url: `${config.gatewayUrl}${request.url}` }));
};
