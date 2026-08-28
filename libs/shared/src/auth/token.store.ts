import { Injectable, PLATFORM_ID, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

const STORAGE_KEY = 'samaajconnect.token';
const REFRESH_KEY = 'samaajconnect.refresh';

/**
 * Holds the tokens for this browsing session: the short-lived access token, and
 * the refresh token that buys the next one.
 *
 * sessionStorage rather than localStorage: a shared or family device is common
 * in this platform's audience, and a token that survives closing the tab is a
 * longer window than a community app needs. That applies to the refresh token
 * especially - it is the credential that can mint access tokens for a fortnight,
 * so leaving it in localStorage on a shared device would be the worst of both.
 * Closing the tab ends the session on that device; the row on the server lives
 * until it expires or is revoked.
 *
 * Not a cookie, deliberately. A cookie would be sent automatically on every
 * request, which is what makes cookies useful and what makes them need CSRF
 * protection - and this platform has none. A token read from storage and
 * attached by an interceptor is only ever sent because our own code sent it.
 *
 * Every read is guarded because storage throws in private-browsing modes and
 * does not exist at all during server-side rendering.
 */
@Injectable({ providedIn: 'root' })
export class TokenStore {
  private readonly platformId = inject(PLATFORM_ID);
  private readonly current = signal<string | null>(null);

  private readonly currentRefresh = signal<string | null>(null);

  readonly token = this.current.asReadonly();
  readonly refreshToken = this.currentRefresh.asReadonly();

  constructor() {
    this.current.set(this.read(STORAGE_KEY));
    this.currentRefresh.set(this.read(REFRESH_KEY));
  }

  get isSignedIn(): boolean {
    return this.current() !== null;
  }

  /**
   * Stores both halves of a session. `refreshToken` is omitted only by callers
   * that genuinely have no refresh token to store.
   */
  set(token: string, refreshToken?: string | null): void {
    this.current.set(token);
    this.withStorage((storage) => storage.setItem(STORAGE_KEY, token));

    if (refreshToken === undefined) {
      return;
    }

    this.currentRefresh.set(refreshToken);

    this.withStorage((storage) => {
      if (refreshToken === null) {
        storage.removeItem(REFRESH_KEY);
      } else {
        storage.setItem(REFRESH_KEY, refreshToken);
      }
    });
  }

  clear(): void {
    this.current.set(null);
    this.currentRefresh.set(null);

    this.withStorage((storage) => {
      storage.removeItem(STORAGE_KEY);
      storage.removeItem(REFRESH_KEY);
    });
  }

  private read(key: string): string | null {
    let found: string | null = null;

    this.withStorage((storage) => {
      found = storage.getItem(key);
    });

    return found;
  }

  private withStorage(action: (storage: Storage) => void): void {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    try {
      action(window.sessionStorage);
    } catch {
      // Private browsing, or storage disabled. Signing in still works for the
      // life of the page; it just will not survive a reload.
    }
  }
}
