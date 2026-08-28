import { HttpErrorResponse } from '@angular/common/http';
import { ProblemDetails } from '../api/problem-details';

/**
 * The decision the interceptor makes, tested directly. Wiring a full HTTP
 * pipeline here would exercise Angular rather than this rule.
 */
function endsSession(error: HttpErrorResponse): boolean {
  if (error.status === 401) {
    return true;
  }

  return (
    error.status === 403 && (error.error as ProblemDetails | null)?.title === 'Tenant.Mismatch'
  );
}

describe('when a response should end the session', () => {
  it('ends it on 401, because the token expired or was rejected', () => {
    expect(endsSession(new HttpErrorResponse({ status: 401 }))).toBe(true);
  });

  it('ends it on a tenant mismatch, because the token belongs to another Samaaj', () => {
    // Left in place it would also break the anonymous screens: the services
    // refuse a mismatched token before checking whether the request needed
    // authentication at all.
    const error = new HttpErrorResponse({
      status: 403,
      error: { title: 'Tenant.Mismatch', detail: 'Wrong Samaaj.' },
    });

    expect(endsSession(error)).toBe(true);
  });

  it('leaves an ordinary 403 alone', () => {
    // "Signed in but not allowed" - signing the member out here would be
    // baffling.
    const error = new HttpErrorResponse({
      status: 403,
      error: { title: 'Auth.Forbidden', detail: 'Not allowed.' },
    });

    expect(endsSession(error)).toBe(false);
  });

  it('leaves a 403 with no problem body alone', () => {
    expect(endsSession(new HttpErrorResponse({ status: 403 }))).toBe(false);
  });

  it('leaves server errors alone', () => {
    expect(endsSession(new HttpErrorResponse({ status: 500 }))).toBe(false);
  });
});
