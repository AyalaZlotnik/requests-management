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
import { BulkUpdateResult, RequestDetails, RequestStatus } from '../../core/models/request.models';
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
  templateUrl: './requests-page.component.html'
  ,
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

  protected onRequestChanged(request: RequestDetails): void {
    this.store.updateSelectedVersion(request);
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
