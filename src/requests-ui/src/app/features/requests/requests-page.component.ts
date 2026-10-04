import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ConnectionStatus } from '../../core/api/connection-status.service';
import { errorMessage } from '../../core/api/http-error';
import { CurrentUser } from '../../core/current-user.service';
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
  // Material closes the drawer on Esc only while focus is inside it; after clicking a row focus stays in the list.
  host: { '(document:keydown.escape)': 'closeDetailsOnEscape()' },
  template: `
    <!-- Details open in a drawer over the list, so the table keeps its full width. Esc or a click outside closes it. -->
    <mat-drawer-container class="page">
      <mat-drawer
        class="details-drawer"
        position="end"
        mode="over"
        [opened]="store.selectedId() !== null"
        (closedStart)="store.openRequest(null)"
      >
        @if (store.selectedId(); as id) {
          <app-request-details [requestId]="id" (changed)="onRequestChanged()" (closed)="store.openRequest(null)" />
        }
      </mat-drawer>

      <mat-drawer-content class="page-content">
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
            עדכון מרוכז: {{ r.succeeded }} עודכנו, {{ r.failed }} לא עודכנו.
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
  private readonly dialog = inject(MatDialog);

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

  protected closeDetailsOnEscape(): void {
    // With the conflict dialog open, Esc belongs to the dialog.
    if (this.store.selectedId() !== null && this.dialog.openDialogs.length === 0) {
      this.store.openRequest(null);
    }
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

  protected failedItems(result: BulkUpdateResult) {
    return result.results.filter((r) => r.outcome !== 'Updated');
  }
}
