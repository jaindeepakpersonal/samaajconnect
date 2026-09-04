import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ElementRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthedImageDirective } from './authed-image.directive';

/**
 * The directive is driven directly rather than through a host component's
 * template, and that is a constraint of where these tests live rather than a
 * preference. `npm run test:libs` is plain Vitest with no Angular compiler — see
 * this library's CLAUDE.md — so a spec here cannot compile a template. Setting
 * the input by hand exercises exactly the same setter Angular would call.
 */
describe('AuthedImageDirective', () => {
  let http: HttpTestingController;
  let img: HTMLImageElement;
  let created: string[];
  let revoked: string[];
  let realCreate: typeof URL.createObjectURL;
  let realRevoke: typeof URL.revokeObjectURL;

  function directive(): AuthedImageDirective {
    return TestBed.runInInjectionContext(() => new AuthedImageDirective());
  }

  beforeEach(() => {
    created = [];
    revoked = [];
    img = document.createElement('img');

    // jsdom implements neither of these, so they are replaced rather than
    // spied on. Only the two statics move — swapping the whole `URL` global for
    // an object literal is what the first version did, and it does not
    // type-check under the app builds that also compile this file (see this
    // library's CLAUDE.md).
    //
    // Recording the URLs is how the lifetime assertions work at all: leaking
    // one is the failure this directive exists to avoid.
    let next = 0;
    realCreate = URL.createObjectURL;
    realRevoke = URL.revokeObjectURL;

    URL.createObjectURL = vi.fn(() => {
      const url = `blob:test/${next++}`;
      created.push(url);
      return url;
    });

    URL.revokeObjectURL = vi.fn((url: string) => {
      revoked.push(url);
    });

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ElementRef, useValue: new ElementRef(img) },
      ],
    });

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    URL.createObjectURL = realCreate;
    URL.revokeObjectURL = realRevoke;
  });

  function make(path: string | null): AuthedImageDirective {
    const instance = directive();
    instance.scAuthedSrc = path;
    return instance;
  }

  /**
   * The reason the directive exists. An `<img src>` set by a template is
   * fetched by the browser with no Authorization header; going through
   * HttpClient is what puts the request through the auth interceptor.
   */
  it('fetches the photo through HttpClient rather than letting the tag do it', () => {
    make('/v1/members/abc/photo');

    const request = http.expectOne('/v1/members/abc/photo');

    expect(request.request.responseType).toBe('blob');
    expect(request.request.method).toBe('GET');

    // Nothing is on the element until the bytes arrive, so the browser never
    // issues an unauthenticated request of its own.
    expect(img.getAttribute('src')).toBeNull();
  });

  it('shows the bytes it was given', () => {
    make('/v1/members/abc/photo');

    http.expectOne('/v1/members/abc/photo').flush(new Blob(['x']));

    expect(img.src).toContain('blob:test/0');
  });

  it('asks for nothing when the member has no photo', () => {
    make(null);
    make('');
    make('   ');

    http.verify();
    expect(img.getAttribute('src')).toBeNull();
  });

  /**
   * A 404 is the ordinary answer for a member with no photo and a 403 is one
   * this screen should respect quietly. Either way there is no image and the
   * alt text stands in.
   */
  it('leaves the element empty when the photo is refused or missing', () => {
    make('/v1/members/abc/photo');

    http.expectOne('/v1/members/abc/photo')
      .flush(null, { status: 404, statusText: 'Not Found' });

    expect(img.getAttribute('src')).toBeNull();
  });

  /**
   * Angular re-runs an input binding on every change-detection pass. Without
   * the guard, one directory page would issue an unbounded number of requests.
   */
  it('does not refetch when the same path is set again', () => {
    const instance = make('/v1/members/abc/photo');
    http.expectOne('/v1/members/abc/photo').flush(new Blob(['x']));

    instance.scAuthedSrc = '/v1/members/abc/photo';
    instance.scAuthedSrc = '/v1/members/abc/photo';

    http.verify();
  });

  it('revokes the previous object URL when the photo changes', () => {
    const instance = make('/v1/members/abc/photo');
    http.expectOne('/v1/members/abc/photo').flush(new Blob(['first']));

    instance.scAuthedSrc = '/v1/members/def/photo';

    expect(revoked).toContain(created[0]);

    http.expectOne('/v1/members/def/photo').flush(new Blob(['second']));
  });

  /**
   * Object URLs are held by the document until revoked, so a directory somebody
   * scrolls through would otherwise keep every face it had ever shown.
   */
  it('revokes the object URL when the element goes away', () => {
    const instance = make('/v1/members/abc/photo');
    http.expectOne('/v1/members/abc/photo').flush(new Blob(['x']));

    instance.ngOnDestroy();

    expect(revoked).toContain(created[0]);
  });

  /**
   * Two requests in flight, the second answering first. Adopting a late
   * response would show the wrong person's face, which is worse than showing
   * none at all.
   */
  it('ignores a response that arrives after the photo has changed', () => {
    const instance = make('/v1/members/abc/photo');
    const first = http.expectOne('/v1/members/abc/photo');

    instance.scAuthedSrc = '/v1/members/def/photo';
    http.expectOne('/v1/members/def/photo').flush(new Blob(['right']));

    const shown = img.src;

    first.flush(new Blob(['stale']));

    expect(img.src).toBe(shown);
  });
});
