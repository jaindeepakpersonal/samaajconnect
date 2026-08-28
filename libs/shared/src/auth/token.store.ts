import { Injectable, PLATFORM_ID, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

const STORAGE_KEY = 'samaajconnect.token';

/**
 * Holds the access token for this browsing session.
 *
 * sessionStorage rather than localStorage: a shared or family device is common
 * in this platform's audience, and a token that survives closing the tab is a
 * longer window than a community app needs. Every read is guarded because
 * storage throws in private-browsing modes and does not exist at all during
 * server-side rendering.
 */
@Injectable({ providedIn: 'root' })
export class TokenStore {
  private readonly platformId = inject(PLATFORM_ID);
  private readonly current = signal<string | null>(null);

  readonly token = this.current.asReadonly();

  constructor() {
    this.current.set(this.read());
  }

  get isSignedIn(): boolean {
    return this.current() !== null;
  }

  set(token: string): void {
    this.current.set(token);

    this.withStorage((storage) => storage.setItem(STORAGE_KEY, token));
  }

  clear(): void {
    this.current.set(null);

    this.withStorage((storage) => storage.removeItem(STORAGE_KEY));
  }

  private read(): string | null {
    let found: string | null = null;

    this.withStorage((storage) => {
      found = storage.getItem(STORAGE_KEY);
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
