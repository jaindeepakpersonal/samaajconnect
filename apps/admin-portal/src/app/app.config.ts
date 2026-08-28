import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { API_CONFIG, authInterceptor, tenantInterceptor } from '@samaajconnect/shared';

import { routes } from './app.routes';
import { adminScopeInterceptor } from './core/admin-scope';
import { environment } from '../environments/environment';

/**
 * No SSR here, unlike member-portal. Every screen in this app is behind a
 * login and reads live administrative data, so there is nothing a server
 * render could produce but an empty shell (root `CLAUDE.md` §2: the admin
 * panel is an SPA).
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),

    // Order matters. tenant resolves the URL; adminScope adds the Super Admin
    // override for the selected Samaaj; auth attaches the token last, to
    // whatever request came out of the two before it.
    provideHttpClient(
      withFetch(),
      withInterceptors([tenantInterceptor, adminScopeInterceptor, authInterceptor]),
    ),

    { provide: API_CONFIG, useValue: environment.api },
  ],
};
