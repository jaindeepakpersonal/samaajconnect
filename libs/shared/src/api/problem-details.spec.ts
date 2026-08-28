import { HttpErrorResponse } from '@angular/common/http';
import { describeError, fieldErrors } from './problem-details';

function problem(status: number, body: unknown): HttpErrorResponse {
  return new HttpErrorResponse({ status, error: body });
}

describe('describeError', () => {
  it('passes the API detail through rather than inventing its own copy', () => {
    // The services deliberately return one identical message for an unknown
    // account and a wrong password; rewriting it here would undo that.
    const message = describeError(
      problem(401, { title: 'Auth.InvalidCredentials', detail: 'Incorrect mobile/email or password.' }),
    );

    expect(message).toBe('Incorrect mobile/email or password.');
  });

  it('prefers the first field error when the response is a validation problem', () => {
    const message = describeError(
      problem(400, { errors: { Password: ['Password must be at least 10 characters.'] } }),
    );

    expect(message).toBe('Password must be at least 10 characters.');
  });

  it('explains a connection failure rather than showing a bare status code', () => {
    expect(describeError(problem(0, null))).toContain('Could not reach the server');
  });

  it('falls back to something readable when the body is not a problem document', () => {
    expect(describeError(problem(500, 'boom'))).toBe('Something went wrong. Please try again.');
  });

  it('handles a non-HTTP failure', () => {
    expect(describeError(new Error('kaboom'))).toBe('Something went wrong. Please try again.');
  });
});

describe('fieldErrors', () => {
  it('returns the field map from a validation problem', () => {
    expect(fieldErrors(problem(400, { errors: { Slug: ['required'] } }))).toEqual({
      Slug: ['required'],
    });
  });

  it('returns an empty map when there is nothing field-level', () => {
    expect(fieldErrors(problem(409, { detail: 'taken' }))).toEqual({});
    expect(fieldErrors('not an error')).toEqual({});
  });
});
