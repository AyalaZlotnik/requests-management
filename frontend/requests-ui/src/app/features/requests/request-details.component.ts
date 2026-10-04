import { DatePipe } from '@angular/common';
import { Component, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Subject, catchError, combineLatest, forkJoin, map, of, startWith, switchMap, tap } from 'rxjs';
import { RequestsApi } from '../../core/api/requests-api.service';
import { errorMessage, isConflict } from '../../core/api/http-error';
import { CurrentUser } from '../../core/current-user.service';
import { RequestDetails, RequestStatus, StatusHistoryEntry } from '../../core/models/request.models';
import { StatusLabelPipe } from './status-label.pipe';

type Loaded = { details: RequestDetails; history: StatusHistoryEntry[] };

/**
 * Details side panel: shows a request, lets the user change its status and shows the audit history.
 * On 409 Conflict the latest version is re-loaded and the user is told to review and retry –
 * the server is the one enforcing concurrency; the UI only explains it.
 */
@Component({
  selector: 'app-request-details',
  imports: [DatePipe, StatusLabelPipe],
  template: `
    <aside class="details" aria-label="Request details">
      <header>
        <h2>Request #{{ requestId() }}</h2>
        <button type="button" class="icon" aria-label="Close" (click)="closed.emit()">✕</button>
      </header>

      @if (loadError()) {
        <p class="error-text">{{ loadError() }}</p>
      } @else if (data(); as d) {
        <dl>
          <dt>Title</dt><dd>{{ d.details.title }}</dd>
          <dt>Organization</dt><dd>{{ d.details.organizationName }}</dd>
          <dt>Status</dt><dd><span class="badge" [attr.data-status]="d.details.status">{{ d.details.status | statusLabel }}</span></dd>
          <dt>Priority</dt><dd>{{ d.details.priority }}</dd>
          <dt>Assigned to</dt><dd>{{ d.details.assignedTo ?? '—' }}</dd>
          <dt>Created</dt><dd>{{ d.details.createdAt | date: 'dd/MM/yyyy HH:mm:ss' }}</dd>
          <dt>Updated</dt><dd>{{ d.details.updatedAt | date: 'dd/MM/yyyy HH:mm:ss' }}</dd>
        </dl>

        <section class="status-change">
          <h3>Change status</h3>
          @if (d.details.allowedNextStatuses.length) {
            <div class="row">
              <select #next aria-label="New status">
                @for (s of d.details.allowedNextStatuses; track s) {
                  <option [value]="s">{{ s | statusLabel }}</option>
                }
              </select>
              <button type="button" class="primary" [disabled]="saving()" (click)="changeStatus($any(next.value))">
                {{ saving() ? 'Saving…' : 'Update' }}
              </button>
            </div>
          } @else {
            <p class="muted">No status change is allowed from {{ d.details.status | statusLabel }}.</p>
          }
          @if (conflict()) {
            <p class="warning" role="alert">
              This request was changed by another user. The latest version is now shown – review it and try again.
            </p>
          }
          @if (saveError()) {
            <p class="error-text" role="alert">{{ saveError() }}</p>
          }
          @if (saved()) {
            <p class="success" role="status">Status updated.</p>
          }
        </section>

        <section>
          <h3>History</h3>
          <ol class="history">
            @for (h of d.history; track h.id) {
              <li>
                <span>{{ h.previousStatus | statusLabel }} → <strong>{{ h.newStatus | statusLabel }}</strong></span>
                <span class="muted small">{{ h.changedBy }} · {{ h.changedAt | date: 'dd/MM/yyyy HH:mm:ss' }}</span>
              </li>
            } @empty {
              <li class="muted">No status changes yet.</li>
            }
          </ol>
        </section>
      } @else {
        <p class="muted">Loading…</p>
      }
    </aside>
  `,
})
export class RequestDetailsComponent {
  private readonly api = inject(RequestsApi);
  private readonly user = inject(CurrentUser);
  private readonly refresh$ = new Subject<void>();

  readonly requestId = input.required<number>();
  readonly changed = output<RequestDetails>();
  readonly closed = output<void>();

  protected readonly data = signal<Loaded | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly conflict = signal(false);
  protected readonly saveError = signal<string | null>(null);

  constructor() {
    // Switching to another request cancels the pending load of the previous one.
    const id$ = toObservable(this.requestId).pipe(
      tap(() => {
        this.data.set(null);
        this.resetMessages();
      }),
    );
    combineLatest([id$, this.refresh$.pipe(startWith(undefined))])
      .pipe(
        switchMap(([id]) =>
          forkJoin({ details: this.api.getById(id), history: this.api.getHistory(id) }).pipe(
            map((loaded) => ({ loaded, error: null })),
            catchError((e) => of({ loaded: null, error: errorMessage(e) })),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ loaded, error }) => {
        this.loadError.set(error);
        if (loaded) this.data.set(loaded);
      });
  }

  protected changeStatus(status: RequestStatus): void {
    const current = this.data()?.details;
    if (!current) return;

    this.resetMessages();
    this.saving.set(true);
    this.api.updateStatus(current.id, status, current.rowVersion, this.user.name()).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.saved.set(true);
        this.refresh$.next();
        this.changed.emit(updated);
      },
      error: (e) => {
        this.saving.set(false);
        if (isConflict(e)) {
          this.conflict.set(true);
          this.refresh$.next();
        } else {
          this.saveError.set(errorMessage(e));
        }
      },
    });
  }

  private resetMessages(): void {
    this.saved.set(false);
    this.conflict.set(false);
    this.saveError.set(null);
  }
}
