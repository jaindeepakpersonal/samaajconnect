import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideClientHydration, withEventReplay } from '@angular/platform-browser';
import { API_CONFIG, authInterceptor, tenantInterceptor } from '@samaajconnect/shared';

import { routes } from './app.routes';
import { environment } from '../environments/environment';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    provideClientHydration(withEventReplay()),

    // withFetch so the same HttpClient works during server-side rendering.
    // Interceptor order matters: tenant resolves the URL, auth attaches the
    // token to whatever URL came out of it.
    provideHttpClient(withFetch(), withInterceptors([tenantInterceptor, authInterceptor])),

    { provide: API_CONFIG, useValue: environment.api },
  ],
};
