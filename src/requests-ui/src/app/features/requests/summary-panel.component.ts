import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, map, of, switchMap, tap } from 'rxjs';
import { RequestsApi } from '../../core/api/requests-api.service';
import { errorMessage } from '../../core/api/http-error';
import { RequestsSummary } from '../../core/models/request.models';
import { StatusLabelPipe } from './status-label.pipe';

/** Server-side aggregations (cached by the API). Re-fetched whenever `refreshKey` changes. */
@Component({
  selector: 'app-summary-panel',
  imports: [DecimalPipe, DatePipe, StatusLabelPipe],
  template: `
    <section class="summary" aria-label="Summary">
      @if (error()) {
        <p class="error-text">Summary unavailable: {{ error() }}</p>
      } @else if (summary(); as s) {
        <div class="card">
          <h3>Total</h3>
          <p class="big">{{ s.totalCount | number }}</p>
          <p class="muted">{{ s.openCount | number }} open</p>
        </div>
        <div class="card">
          <h3>By status</h3>
          @for (item of s.byStatus; track item.key) {
            <div class="bar-row">
              <span>{{ item.key | statusLabel }}</span>
              <span class="bar"><span [style.width.%]="percent(item.count, s.totalCount)" [attr.data-status]="item.key"></span></span>
              <span class="num">{{ item.count | number }}</span>
            </div>
          }
        </div>
        <div class="card">
          <h3>Open by priority</h3>
          @for (item of s.openByPriority; track item.key) {
            <div class="bar-row">
              <span>{{ item.key }}</span>
              <span class="bar"><span [style.width.%]="percent(item.count, s.openCount)" [attr.data-priority]="item.key"></span></span>
              <span class="num">{{ item.count | number }}</span>
            </div>
          }
        </div>
        <div class="card">
          <h3>Top handlers (open)</h3>
          <ol class="plain">
            @for (item of s.topAssigneesByOpenRequests; track item.key) {
              <li><span>{{ item.key }}</span><span class="num">{{ item.count | number }}</span></li>
            } @empty {
              <li class="muted">No assigned open requests</li>
            }
          </ol>
          <p class="muted small">Updated {{ s.generatedAt | date: 'HH:mm:ss' }}</p>
        </div>
      } @else {
        <p class="muted">Loading summary…</p>
      }
    </section>
  `,
})
export class SummaryPanelComponent {
  private readonly api = inject(RequestsApi);

  /** Increment to force a reload (e.g. after a status change). */
  readonly refreshKey = input(0);

  protected readonly summary = signal<RequestsSummary | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    toObservable(this.refreshKey)
      .pipe(
        tap(() => this.error.set(null)),
        switchMap(() =>
          this.api.getSummary().pipe(
            map((summary) => ({ summary, error: null })),
            catchError((e) => of({ summary: null, error: errorMessage(e) })),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ summary, error }) => {
        if (summary) this.summary.set(summary);
        this.error.set(error);
      });
  }

  protected percent(count: number, total: number): number {
    return total ? (count / total) * 100 : 0;
  }
}
