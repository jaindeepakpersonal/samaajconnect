import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { API_CONFIG } from '../api/api-config';

/**
 * Rewrites relative API URLs to the gateway.
 *
 * Note what this deliberately does *not* do: attach a tenant header.
 * ARCHITECTURE.md section 7 sketches "an explicit header override" for local
 * development, but the gateway strips every inbound tenant header before
 * routing - that is the control stopping a client from choosing its own
 * Samaaj, and a dev-only exception to it would be a hole with a friendly name.
 *
 * Local development uses the Angular dev-server proxy instead
 * (`apps/member-portal/proxy.conf.json`), which sets the Host header the
 * gateway already resolves subdomains from. Same effect, no new trusted input.
 */
export const tenantInterceptor: HttpInterceptorFn = (request, next) => {
  const config = inject(API_CONFIG);

  if (/^https?:\/\//i.test(request.url)) {
    return next(request);
  }

  return next(request.clone({ url: `${config.gatewayUrl}${request.url}` }));
};
