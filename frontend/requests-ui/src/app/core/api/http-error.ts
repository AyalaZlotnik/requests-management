import { HttpErrorResponse } from '@angular/common/http';

/** Builds a user-facing message from a Problem Details response. */
export function errorMessage(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Unexpected error.';
  }
  if (error.status === 0) {
    return 'The server is not reachable. Check that the API is running.';
  }

  const problem = error.error as { title?: string; detail?: string; errors?: Record<string, string[]> } | null;
  if (problem?.errors) {
    return Object.values(problem.errors).flat().join(' ');
  }
  return problem?.detail ?? problem?.title ?? `Request failed (${error.status}).`;
}

export function isConflict(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 409;
}
