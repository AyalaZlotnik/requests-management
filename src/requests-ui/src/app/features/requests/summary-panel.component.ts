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
  template: `
    <section class="summary-strip" aria-label="נתונים מסכמים" [class.busy]="loading()">
      @if (error()) {
        <p class="error-text" role="alert">{{ error() }}</p>
      } @else if (summary(); as s) {
        <div class="kpi">
          <span class="kpi-value">{{ s.total | number }}</span>
          <span class="kpi-label">{{ agree(s.total, 'פנייה', 'פניות') }}</span>
        </div>
        <div class="kpi">
          <span class="kpi-value warn">{{ s.openOlderThan7Days | number }}</span>
          <span class="kpi-label">{{ agree(s.openOlderThan7Days, 'פתוחה', 'פתוחות') }} מעל 7 ימים</span>
        </div>

        <div class="status-counts" role="group" aria-label="סינון לפי סטטוס">
          @for (item of s.byStatus; track item.key) {
            <button
              type="button"
              class="status-count"
              [class.selected]="isOnlyStatus(item.key)"
              [attr.aria-pressed]="isOnlyStatus(item.key)"
              [title]="isOnlyStatus(item.key) ? 'ביטול הסינון לפי סטטוס' : 'הצגת פניות בסטטוס הזה בלבד'"
              (click)="statusClick.emit(item.key)"
            >
              <span class="dot" [attr.data-status]="item.key"></span>
              {{ item.key | statusLabel }}
              <strong>{{ item.count | number }}</strong>
            </button>
          }
        </div>

        <span class="spacer"></span>
        @if (s.lastUpdatedAt) {
          <span class="muted small">עדכון אחרון {{ s.lastUpdatedAt | date: 'dd/MM/yy HH:mm' }}</span>
        }
        <button mat-button type="button" [attr.aria-expanded]="expanded()" (click)="expanded.set(!expanded())">
          {{ expanded() ? 'הסתרת פילוחים' : 'פילוחים' }}
        </button>
      } @else {
        <mat-progress-bar mode="indeterminate" aria-label="טוען נתונים מסכמים" />
      }
    </section>

    @if (expanded() && summary(); as s) {
      <section class="breakdowns" aria-label="פילוחים">
        <div class="card">
          <h3>לפי עדיפות</h3>
          @for (item of s.byPriority; track item.key) {
            <div class="bar-row" [class.dim]="priorityFiltered() && !filters().priority.includes(item.key)">
              <span>{{ item.key | priorityLabel }}</span>
              <span class="bar"><span [style.width.%]="percent(item.count, maxPriority())" [attr.data-priority]="item.key"></span></span>
              <span class="num">{{ item.count | number }}</span>
            </div>
          }
          @if (priorityFiltered()) {
            <p class="muted small">מוצגות כל העדיפויות, לפי שאר הסינונים.</p>
          }
        </div>
        <div class="card">
          <h3>מטפלים עם הכי הרבה פניות פתוחות</h3>
          <ol class="plain">
            @for (item of s.topAssignees; track item.key) {
              <li><span>{{ item.key }}</span><span class="num">{{ item.count | number }}</span></li>
            } @empty {
              <li class="muted">אין פניות פתוחות משויכות</li>
            }
          </ol>
        </div>
      </section>
    }
  `,
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
