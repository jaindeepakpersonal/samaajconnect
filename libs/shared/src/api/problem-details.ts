import { HttpErrorResponse } from '@angular/common/http';

/** RFC 9457 problem document, as every service returns via ResultExtensions. */
export interface ProblemDetails {
  readonly title?: string;
  readonly detail?: string;
  readonly status?: number;
  readonly errors?: Record<string, string[]>;
}

/**
 * Turns a failed response into something worth showing a member.
 *
 * The services deliberately return one identical message for several
 * conditions (an unknown account and a wrong password, for instance), so this
 * passes `detail` through rather than inventing its own copy per status code.
 */
export function describeError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Something went wrong. Please try again.';
  }

  if (error.status === 0) {
    return 'Could not reach the server. Check your connection and try again.';
  }

  const problem = error.error as ProblemDetails | null;

  if (problem?.errors) {
    const first = Object.values(problem.errors).flat()[0];

    if (first) {
      return first;
    }
  }

  return problem?.detail ?? 'Something went wrong. Please try again.';
}

/** Field-level validation messages, keyed by the field name the API used. */
export function fieldErrors(error: unknown): Record<string, string[]> {
  if (!(error instanceof HttpErrorResponse)) {
    return {};
  }

  return (error.error as ProblemDetails | null)?.errors ?? {};
}
