import { Injectable, PLATFORM_ID, inject } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

const DEVICE_KEY = 'samaajconnect.device';

/**
 * The token that tells identity-tenant-service this browser passed the
 * sign-in code, and may skip it for thirty days.
 *
 * **`localStorage`, where `TokenStore` deliberately uses `sessionStorage`.**
 * A trust that ended with the tab would be no trust at all - it would ask for
 * a code every visit, which is the thing a member ticked a box to stop. The
 * difference in what each is worth is what makes that safe: a session token
 * *is* the member, while this excuses only the code - the password is still
 * asked for every time, so a stolen device token opens nothing on its own.
 *
 * Kept per origin, like the session, so trusting the member portal does not
 * trust the admin panel. The server forgets every device when the member
 * changes or resets their password or signs out everywhere; a token it has
 * forgotten is simply answered with a code, so nothing here needs clearing
 * then. Every access is guarded, as in `TokenStore`, for private browsing and
 * server-side rendering.
 */
@Injectable({ providedIn: 'root' })
export class DeviceTrustStore {
  private readonly platformId = inject(PLATFORM_ID);

  get(): string | null {
    let found: string | null = null;

    this.withStorage((storage) => {
      found = storage.getItem(DEVICE_KEY);
    });

    return found;
  }

  set(deviceToken: string): void {
    this.withStorage((storage) => storage.setItem(DEVICE_KEY, deviceToken));
  }

  clear(): void {
    this.withStorage((storage) => storage.removeItem(DEVICE_KEY));
  }

  private withStorage(action: (storage: Storage) => void): void {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    try {
      action(window.localStorage);
    } catch {
      // Private browsing, or storage disabled: the device is simply not
      // remembered, and the next sign-in asks for a code.
    }
  }
}
