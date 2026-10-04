import { Component, inject, signal } from '@angular/core';
import { errorMessage } from '../../core/api/http-error';
import { CurrentUser } from '../../core/current-user.service';
import { BulkUpdateResult, RequestStatus } from '../../core/models/request.models';
import { BulkStatusBarComponent } from './bulk-status-bar.component';
import { PaginationComponent } from './pagination.component';
import { RequestDetailsComponent } from './request-details.component';
import { RequestFiltersComponent } from './request-filters.component';
import { RequestsListStore } from './requests-list.store';
import { RequestsTableComponent } from './requests-table.component';
import { SummaryPanelComponent } from './summary-panel.component';

/** Container: wires the store to the presentational components. */
@Component({
  selector: 'app-requests-page',
  imports: [
    SummaryPanelComponent,
    RequestFiltersComponent,
    RequestsTableComponent,
    PaginationComponent,
    RequestDetailsComponent,
    BulkStatusBarComponent,
  ],
  providers: [RequestsListStore],
  template: `
    <app-summary-panel [refreshKey]="summaryRefresh()" />

    <div class="layout" [class.with-details]="activeId() !== null">
      <section class="list">
        <app-request-filters (filtersChange)="store.setFilters($event)" />

        @if (store.selection().size > 0) {
          <app-bulk-status-bar
            [count]="store.selection().size"
            [busy]="bulkBusy()"
            (apply)="bulkUpdate($event)"
            (clear)="store.clearSelection()" />
        }
        @if (bulkResult(); as r) {
          <div class="notice" [class.warning]="r.failed > 0" role="status">
            Bulk update: {{ r.succeeded }} updated, {{ r.failed }} not updated.
            @if (r.failed > 0) {
              <ul>
                @for (item of failedItems(r); track item.id) {
                  <li>#{{ item.id }} – {{ item.outcome }}: {{ item.error }}</li>
                }
              </ul>
            }
            <button type="button" class="link" (click)="bulkResult.set(null)">Dismiss</button>
          </div>
        }
        @if (bulkError()) {
          <p class="error-text" role="alert">{{ bulkError() }}</p>
        }

        @if (store.error(); as error) {
          <div class="state error" role="alert">
            <p>Could not load requests: {{ error }}</p>
            <button type="button" (click)="store.reload()">Retry</button>
          </div>
        } @else if (store.isEmpty()) {
          <div class="state">No requests match the current filters.</div>
        } @else if (store.result(); as result) {
          <app-requests-table
            [items]="result.items"
            [sortBy]="store.query().sortBy"
            [sortDirection]="store.query().sortDirection"
            [selectedIds]="store.selectedIds()"
            [activeId]="activeId()"
            [loading]="store.loading()"
            (sort)="store.sortBy($event)"
            (open)="activeId.set($event)"
            (toggleSelect)="store.toggleSelection($event)"
            (toggleAll)="store.toggleAllOnPage()" />
          <app-pagination
            [page]="result.page"
            [pageSize]="result.pageSize"
            [totalPages]="result.totalPages"
            [totalCount]="result.totalCount"
            (pageChange)="store.setPage($event)"
            (pageSizeChange)="store.setPageSize($event)" />
        }
        @if (store.loading()) {
          <div class="state loading" aria-live="polite">Loading…</div>
        }
      </section>

      @if (activeId(); as id) {
        <app-request-details [requestId]="id" (changed)="onRequestChanged()" (closed)="activeId.set(null)" />
      }
    </div>
  `,
})
export class RequestsPageComponent {
  protected readonly store = inject(RequestsListStore);
  private readonly user = inject(CurrentUser);

  protected readonly activeId = signal<number | null>(null);
  protected readonly summaryRefresh = signal(0);
  protected readonly bulkBusy = signal(false);
  protected readonly bulkResult = signal<BulkUpdateResult | null>(null);
  protected readonly bulkError = signal<string | null>(null);

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

  protected failedItems(result: BulkUpdateResult) {
    return result.results.filter((r) => r.outcome !== 'Updated');
  }
}
