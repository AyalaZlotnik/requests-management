import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { catchError, combineLatest, map, of, switchMap, tap } from 'rxjs';
import { RequestsApi } from '../../core/api/requests-api.service';
import { errorMessage } from '../../core/api/http-error';
import { RequestFilters, RequestsSummary } from '../../core/models/request.models';
import { PriorityLabelPipe, StatusLabelPipe } from './status-label.pipe';

/**
 * Server-side aggregations for the current filter. Re-fetched when the filter changes or after an update
 * (refreshKey); switchMap drops an older response if a newer filter arrives first.
 */
@Component({
  selector: 'app-summary-panel',
  imports: [DecimalPipe, DatePipe, MatProgressBarModule, StatusLabelPipe, PriorityLabelPipe],
  template: `
    <section class="summary" aria-label="נתונים מסכמים" [class.busy]="loading()">
      @if (error()) {
        <p class="error-text" role="alert">לא ניתן לטעון את הנתונים המסכמים: {{ error() }}</p>
      } @else if (summary(); as s) {
        <div class="card">
          <h3>פניות התואמות לסינון</h3>
          <p class="big">{{ s.total | number }}</p>
          <p class="muted small">
            {{ s.openOlderThan7Days | number }} פתוחות יותר מ-7 ימים
            @if (s.lastUpdatedAt) {
              <br />עדכון אחרון: {{ s.lastUpdatedAt | date: 'dd/MM/yyyy HH:mm' }}
            }
          </p>
        </div>

        <div class="card">
          <h3>לפי סטטוס</h3>
          @for (item of s.byStatus; track item.key) {
            <div class="bar-row" [class.dim]="statusFiltered() && !filters().status.includes(item.key)">
              <span>{{ item.key | statusLabel }}</span>
              <span class="bar"><span [style.width.%]="percent(item.count, maxStatus())" [attr.data-status]="item.key"></span></span>
              <span class="num">{{ item.count | number }}</span>
            </div>
          }
          @if (statusFiltered()) {
            <p class="muted small">מוצגים כל הסטטוסים, לפי שאר הסינונים.</p>
          }
        </div>

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
          <p class="muted small">חושב ב-{{ s.generatedAt | date: 'HH:mm:ss' }}</p>
        </div>
      } @else {
        <mat-progress-bar mode="indeterminate" aria-label="טוען נתונים מסכמים" />
      }
    </section>
  `,
})
export class SummaryPanelComponent {
  private readonly api = inject(RequestsApi);

  readonly filters = input.required<RequestFilters>();
  /** Increment to force a reload (e.g. after a status change). */
  readonly refreshKey = input(0);

  protected readonly summary = signal<RequestsSummary | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

  protected readonly statusFiltered = computed(() => this.filters().status.length > 0);
  protected readonly priorityFiltered = computed(() => this.filters().priority.length > 0);
  protected readonly maxStatus = computed(() => Math.max(1, ...(this.summary()?.byStatus.map((c) => c.count) ?? [])));
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
            catchError((e) => of({ summary: null, error: errorMessage(e) })),
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

  protected percent(count: number, max: number): number {
    return (count / max) * 100;
  }
}
