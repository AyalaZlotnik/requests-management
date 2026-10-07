import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { catchError, combineLatest, map, of, switchMap, tap } from 'rxjs';
import { RequestsApi } from '../../core/api/requests-api.service';
import { errorMessage, isConnectionError } from '../../core/api/http-error';
import { agree } from '../../core/i18n/hebrew-count';
import { RequestFilters, RequestStatus, RequestsSummary } from '../../core/models/request.models';
import { PriorityLabelPipe, StatusLabelPipe } from './status-label.pipe';

/**
 * Summary for the current filter as one slim strip: total, age, and a count per status that filters
 * the list when clicked. The priority and handler breakdowns are in a panel that opens on demand.
 * Re-fetched when the filter changes or after an update (refreshKey); switchMap keeps the latest only.
 */
@Component({
  selector: 'app-summary-panel',
  imports: [DecimalPipe, DatePipe, MatButtonModule, MatProgressBarModule, StatusLabelPipe, PriorityLabelPipe],
  templateUrl: './summary-panel.component.html'
  ,
})
export class SummaryPanelComponent {
  private readonly api = inject(RequestsApi);

  readonly filters = input.required<RequestFilters>();
  /** Increment to force a reload (e.g. after a status change). */
  readonly refreshKey = input(0);
  /** A status count was clicked – the page decides what that does to the filter. */
  readonly statusClick = output<RequestStatus>();

  protected readonly summary = signal<RequestsSummary | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly expanded = signal(false);
  protected readonly agree = agree;

  protected readonly priorityFiltered = computed(() => this.filters().priority.length > 0);
  protected readonly maxPriority = computed(() => Math.max(1, ...(this.summary()?.byPriority.map((c) => c.count) ?? [])));

  constructor() {
    combineLatest([toObservable(this.filters), toObservable(this.refreshKey)])
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.error.set(null);
        }),
        switchMap(([filters]) =>
          this.api.getSummary(filters).pipe(
            map((summary) => ({ summary, error: null })),
            // No connection: keep the last numbers – the banner at the top already explains they may be stale.
            catchError((e) => of({ summary: null, error: isConnectionError(e) ? null : errorMessage(e) })),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ summary, error }) => {
        this.loading.set(false);
        if (summary) this.summary.set(summary);
        this.error.set(error);
      });
  }

  protected isOnlyStatus(status: RequestStatus): boolean {
    const selected = this.filters().status;
    return selected.length === 1 && selected[0] === status;
  }

  protected percent(count: number, max: number): number {
    return (count / max) * 100;
  }
}
