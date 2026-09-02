import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TokenStore } from './token.store';

/**
 * Where both apps keep a signed-in session.
 *
 * Two things here are load-bearing beyond "it stores a string". The storage
 * keys are what makes running the two portals on separate origins necessary —
 * share an origin and an admin signing in overwrites a member's session in the
 * same tab. And every access is guarded, because sessionStorage throws in
 * private-browsing modes and does not exist at all during server-side
 * rendering, which the member portal does.
 */
describe('TokenStore', () => {
  function build(platformId: string = 'browser'): TokenStore {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [TokenStore, { provide: PLATFORM_ID, useValue: platformId }],
    });

    return TestBed.inject(TokenStore);
  }

  beforeEach(() => sessionStorage.clear());

  it('uses the keys the two apps are kept apart by', () => {
    // sessionStorage is scoped to an origin. These exact keys are why the admin
    // panel is on 4300 and not behind the gateway with the member portal: on
    // one origin, an admin signing in would overwrite a member's session in the
    // same tab and then call every endpoint with the wrong token.
    build().set('access-token', 'refresh-token');

    expect(sessionStorage.getItem('samaajconnect.token')).toBe('access-token');
    expect(sessionStorage.getItem('samaajconnect.refresh')).toBe('refresh-token');
  });

  it('reads an existing session back on construction', () => {
    sessionStorage.setItem('samaajconnect.token', 'from-a-reload');
    sessionStorage.setItem('samaajconnect.refresh', 'refresh-from-a-reload');

    const store = build();

    expect(store.token()).toBe('from-a-reload');
    expect(store.refreshToken()).toBe('refresh-from-a-reload');
    expect(store.isSignedIn).toBe(true);
  });

  it('is signed out when there is nothing stored', () => {
    const store = build();

    expect(store.token()).toBeNull();
    expect(store.isSignedIn).toBe(false);
  });

  it('leaves the refresh token alone when it is not passed', () => {
    // The distinction the signature draws: `undefined` means "I am not talking
    // about the refresh token", and it is what a plain token renewal sends.
    // Treating it as "clear it" would sign the member out at the next renewal.
    const store = build();

    store.set('first', 'refresh-token');
    store.set('renewed');

    expect(store.token()).toBe('renewed');
    expect(store.refreshToken()).toBe('refresh-token');
    expect(sessionStorage.getItem('samaajconnect.refresh')).toBe('refresh-token');
  });

  it('clears the refresh token when it is passed as null', () => {
    // `null` is the caller saying there is no refresh token, which is a
    // different statement from saying nothing.
    const store = build();

    store.set('first', 'refresh-token');
    store.set('second', null);

    expect(store.refreshToken()).toBeNull();
    expect(sessionStorage.getItem('samaajconnect.refresh')).toBeNull();
  });

  it('clears both halves on sign-out', () => {
    const store = build();

    store.set('access-token', 'refresh-token');
    store.clear();

    expect(store.token()).toBeNull();
    expect(store.refreshToken()).toBeNull();
    expect(store.isSignedIn).toBe(false);
    expect(sessionStorage.getItem('samaajconnect.token')).toBeNull();
    expect(sessionStorage.getItem('samaajconnect.refresh')).toBeNull();
  });

  it('signs in for the life of the page when storage throws', () => {
    // Private browsing, or storage switched off. The member can still use the
    // app; the session just does not survive a reload. Throwing here instead
    // would make signing in impossible rather than merely forgetful.
    const setItem = vi
      .spyOn(Storage.prototype, 'setItem')
      .mockImplementation(() => {
        throw new DOMException('denied');
      });

    try {
      const store = build();

      expect(() => store.set('access-token', 'refresh-token')).not.toThrow();
      expect(store.token()).toBe('access-token');
      expect(store.isSignedIn).toBe(true);
    } finally {
      setItem.mockRestore();
    }
  });

  it('survives storage that throws on read', () => {
    const getItem = vi
      .spyOn(Storage.prototype, 'getItem')
      .mockImplementation(() => {
        throw new DOMException('denied');
      });

    try {
      expect(() => build()).not.toThrow();
      expect(build().token()).toBeNull();
    } finally {
      getItem.mockRestore();
    }
  });

  it('touches no storage at all on the server', () => {
    // The member portal is server-side rendered, where `sessionStorage` is not
    // a thing. Reaching for it during SSR would throw on every page render.
    sessionStorage.setItem('samaajconnect.token', 'should-not-be-read');

    const store = build('server');

    expect(store.token()).toBeNull();

    store.set('should-not-be-written');

    expect(sessionStorage.getItem('samaajconnect.token')).toBe('should-not-be-read');

    // The signal still moves, so code that runs on both sides behaves the same.
    expect(store.token()).toBe('should-not-be-written');
  });
});
