import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ConnectionStatus } from '../../core/api/connection-status.service';
import { errorMessage } from '../../core/api/http-error';
import { CurrentUser } from '../../core/current-user.service';
import { REQUESTS, agree, hebrewCount } from '../../core/i18n/hebrew-count';
import { BULK_OUTCOME_LABELS } from '../../core/i18n/labels';
import { BulkUpdateResult, RequestStatus } from '../../core/models/request.models';
import { BulkStatusBarComponent } from './bulk-status-bar.component';
import { PAGE_SIZES, filtersOf, sameValue } from './query-params';
import { RequestDetailsComponent } from './request-details.component';
import { RequestFiltersComponent } from './request-filters.component';
import { RequestsListStore } from './requests-list.store';
import { RequestsTableComponent } from './requests-table.component';
import { SummaryPanelComponent } from './summary-panel.component';

/** Container: wires the store to the presentational components. */
@Component({
  selector: 'app-requests-page',
  imports: [
    MatPaginatorModule,
    MatProgressBarModule,
    MatButtonModule,
    MatSidenavModule,
    SummaryPanelComponent,
    RequestFiltersComponent,
    RequestsTableComponent,
    RequestDetailsComponent,
    BulkStatusBarComponent,
  ],
  providers: [RequestsListStore],
  template: `
    <!-- Details open in a drawer over the list, so the table keeps its full width. Esc or a click outside closes it. -->
    <mat-drawer-container class="page" (backdropClick)="store.openRequest(null)">
      <mat-drawer
        class="details-drawer"
        position="end"
        mode="over"
        [opened]="store.selectedId() !== null"
        disableClose
        (keydown.escape)="closeDetailsOnEscape($event)"
      >
        @if (store.selectedId(); as id) {
          <app-request-details [requestId]="id" (changed)="onRequestChanged()" (closed)="store.openRequest(null)" />
        }
      </mat-drawer>

      <!-- Esc closes the drawer from the list as well as from inside it (see closeDetailsOnEscape). -->
      <mat-drawer-content
        class="page-content"
        [class.with-bulk-bar]="store.selection().size > 0"
        (keydown.escape)="closeDetailsOnEscape($event)"
      >
        <app-summary-panel [filters]="filters()" [refreshKey]="summaryRefresh()" (statusClick)="toggleStatus($event)" />
        <app-request-filters [value]="filters()" (filtersChange)="store.setFilters($event)" />

        @if (store.selection().size > 0) {
          <app-bulk-status-bar
            [count]="store.selection().size"
            [busy]="bulkBusy()"
            (apply)="bulkUpdate($event)"
            (clear)="store.clearSelection()" />
        }
        @if (bulkResult(); as r) {
          <div class="notice" [class.warning]="r.failed > 0" role="status">
            {{ bulkSummary(r) }}
            @if (r.failed > 0) {
              <ul>
                @for (item of failedItems(r); track item.id) {
                  <li>
                    <a href="" (click)="$event.preventDefault(); store.openRequest(item.id)">פנייה #{{ item.id }}</a>
                    – {{ outcomeLabels[item.outcome] }}
                  </li>
                }
              </ul>
            }
            <button mat-button type="button" (click)="bulkResult.set(null)">סגירה</button>
          </div>
        }
        @if (bulkError()) {
          <p class="error-text" role="alert">{{ bulkError() }}</p>
        }

        <div class="results">
          @if (store.loading()) {
            <mat-progress-bar mode="indeterminate" aria-label="טוען פניות" />
          }

          @if (store.error(); as error) {
            @if (connection.offline()) {
              <!-- The connection banner at the top already explains this and offers a retry. -->
              <div class="state" role="status">הרשימה תיטען כשהחיבור לשרת יחזור.</div>
            } @else {
              <div class="state error" role="alert">
                <p>לא ניתן לטעון את הפניות. {{ error }}</p>
                <button mat-stroked-button type="button" (click)="store.reload()">ניסיון חוזר</button>
              </div>
            }
          } @else if (store.isEmpty()) {
            <div class="state" role="status">לא נמצאו פניות התואמות לסינון.</div>
          } @else if (store.result(); as result) {
            <app-requests-table
              [class.busy]="store.loading()"
              [items]="result.items"
              [sortBy]="store.query().sortBy"
              [sortDirection]="store.query().sortDirection"
              [selectedIds]="store.selectedIds()"
              [activeId]="store.selectedId()"
              (sort)="store.sort($event.field, $event.direction)"
              (open)="store.openRequest($event)"
              (toggleSelect)="store.toggleSelection($event)"
              (toggleAll)="store.toggleAllOnPage()" />
            <mat-paginator
              [length]="result.totalCount"
              [pageIndex]="result.page - 1"
              [pageSize]="result.pageSize"
              [pageSizeOptions]="pageSizes"
              showFirstLastButtons
              (page)="onPage($event)" />
          } @else {
            <div class="state" role="status">טוען פניות…</div>
          }
        </div>
      </mat-drawer-content>
    </mat-drawer-container>
  `,
})
export class RequestsPageComponent {
  protected readonly store = inject(RequestsListStore);
  private readonly user = inject(CurrentUser);
  protected readonly connection = inject(ConnectionStatus);

  protected readonly pageSizes = PAGE_SIZES;
  protected readonly outcomeLabels = BULK_OUTCOME_LABELS;
  protected readonly filters = computed(() => filtersOf(this.store.query()), { equal: sameValue });
  protected readonly summaryRefresh = signal(0);
  protected readonly bulkBusy = signal(false);
  protected readonly bulkResult = signal<BulkUpdateResult | null>(null);
  protected readonly bulkError = signal<string | null>(null);

  constructor() {
    // "Try again" in the connection banner reloads everything on this page.
    this.connection.retryRequested.pipe(takeUntilDestroyed()).subscribe(() => {
      this.store.reload();
      this.summaryRefresh.update((n) => n + 1);
    });
  }

  /**
   * Esc closes the details drawer – unless something else used the key: an open dropdown marks it with
   * preventDefault, but only later, in Material's overlay listener on the document. So the check waits until
   * the event has finished dispatching (setTimeout 0). Material's drawer would close regardless, so its own
   * Esc/backdrop handling is off (disableClose) and done here. Dialogs live outside the page and never get here.
   */
  protected closeDetailsOnEscape(event: Event): void {
    setTimeout(() => {
      if (!event.defaultPrevented && this.store.selectedId() !== null) {
        this.store.openRequest(null);
      }
    });
  }

  /** Clicking a status in the summary shows only that status; clicking it again removes the filter. */
  protected toggleStatus(status: RequestStatus): void {
    const current = this.filters();
    const only = current.status.length === 1 && current.status[0] === status;
    this.store.setFilters({ ...current, status: only ? [] : [status] });
  }

  protected onPage(event: PageEvent): void {
    this.store.setPage(event.pageIndex + 1, event.pageSize);
  }

  protected onRequestChanged(): void {
    this.store.reload();
    this.summaryRefresh.update((n) => n + 1);
  }

  protected bulkUpdate(status: RequestStatus): void {
    this.bulkBusy.set(true);
    this.bulkError.set(null);
    this.bulkResult.set(null);
    this.store.bulkUpdate(status, this.user.name()).subscribe({
      next: (result) => {
        this.bulkBusy.set(false);
        this.bulkResult.set(result);
        this.summaryRefresh.update((n) => n + 1);
      },
      error: (e) => {
        this.bulkBusy.set(false);
        this.bulkError.set(errorMessage(e));
      },
    });
  }

  /** "עדכון מרוכז: עודכנו 3 פניות; לא עודכנה פנייה אחת." */
  protected bulkSummary(result: BulkUpdateResult): string {
    const parts = [
      result.succeeded > 0
        ? `${agree(result.succeeded, 'עודכנה', 'עודכנו')} ${hebrewCount(result.succeeded, REQUESTS)}`
        : 'לא עודכנו פניות',
    ];
    if (result.failed > 0) {
      parts.push(`${agree(result.failed, 'לא עודכנה', 'לא עודכנו')} ${hebrewCount(result.failed, REQUESTS)}`);
    }
    return `עדכון מרוכז: ${parts.join('; ')}.`;
  }

  protected failedItems(result: BulkUpdateResult) {
    return result.results.filter((r) => r.outcome !== 'Updated');
  }
}
