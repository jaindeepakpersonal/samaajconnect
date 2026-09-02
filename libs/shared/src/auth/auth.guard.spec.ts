import { PLATFORM_ID, runInInjectionContext, Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { provideRouter } from '@angular/router';
import { authGuard } from './auth.guard';
import { TokenStore } from './token.store';

/**
 * The guard on every member page.
 *
 * A UX convenience only — the endpoints behind these pages re-check roles and
 * permissions server-side, and that is the authorization boundary (root
 * CLAUDE.md §7). What it must get right is the *return* trip: a member who
 * followed a link to a deep page and was asked to sign in should land back
 * where they were going, not on the home page.
 */
describe('authGuard', () => {
  let injector: Injector;
  let tokens: TokenStore;

  beforeEach(() => {
    sessionStorage.clear();

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: PLATFORM_ID, useValue: 'browser' }],
    });

    injector = TestBed.inject(Injector);
    tokens = TestBed.inject(TokenStore);
  });

  function run(url: string): boolean | UrlTree {
    const state = { url } as RouterStateSnapshot;
    const route = {} as ActivatedRouteSnapshot;

    return runInInjectionContext(injector, () => authGuard(route, state)) as boolean | UrlTree;
  }

  it('lets a signed-in member through', () => {
    tokens.set('access-token');

    expect(run('/members')).toBe(true);
  });

  it('sends a signed-out visitor to sign in', () => {
    expect(run('/members')).toBeInstanceOf(UrlTree);
  });

  it('remembers where they were going', () => {
    // Without this a member who followed a link to their family page and was
    // asked to sign in lands on Home, and has to find their way back to a page
    // they had already asked for.
    const tree = run('/pathshala/enrolment/abc') as UrlTree;
    const router = TestBed.inject(Router);

    expect(router.serializeUrl(tree)).toBe('/login?returnUrl=%2Fpathshala%2Fenrolment%2Fabc');
  });

  it('carries the query string of the page they wanted', () => {
    const tree = run('/members?locality=Hiran%20Magri') as UrlTree;

    expect(tree.queryParams['returnUrl']).toBe('/members?locality=Hiran%20Magri');
  });
});
