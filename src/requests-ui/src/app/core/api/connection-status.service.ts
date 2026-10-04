import { HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Subject, tap } from 'rxjs';
import { isConnectionError } from './http-error';

/**
 * Tracks whether the API is reachable, so the app shows one connection banner
 * instead of an error in every panel. Updated by connectionInterceptor on every API call.
 */
@Injectable({ providedIn: 'root' })
export class ConnectionStatus {
  private readonly retry$ = new Subject<void>();

  readonly offline = signal(false);
  /** Emits when the user asks to try again; screens reload their data. */
  readonly retryRequested = this.retry$.asObservable();

  retry(): void {
    this.retry$.next();
  }
}

/** A response from the API means it is reachable; see isConnectionError for what counts as unreachable. */
export const connectionInterceptor: HttpInterceptorFn = (request, next) => {
  const status = inject(ConnectionStatus);
  return next(request).pipe(
    tap({
      // Only a real response counts – the "request sent" event would clear the banner too early.
      next: (event) => {
        if (event instanceof HttpResponse) status.offline.set(false);
      },
      error: (error: unknown) => status.offline.set(isConnectionError(error)),
    }),
  );
};
