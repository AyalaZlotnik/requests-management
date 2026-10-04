import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
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
    SummaryPanelComponent,
    RequestFiltersComponent,
    RequestsTableComponent,
    RequestDetailsComponent,
    BulkStatusBarComponent,
  ],
  providers: [RequestsListStore],
  template: `
    <app-summary-panel [filters]="filters()" [refreshKey]="summaryRefresh()" />

    <div class="layout" [class.with-details]="store.selectedId() !== null">
      <section class="list">
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
            <div class="state error" role="alert">
              <p>לא ניתן לטעון את הפניות: {{ error }}</p>
              <button mat-stroked-button type="button" (click)="store.reload()">ניסיון חוזר</button>
            </div>
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
      </section>

      @if (store.selectedId(); as id) {
        <app-request-details [requestId]="id" (changed)="onRequestChanged()" (closed)="store.openRequest(null)" />
      }
    </div>
  `,
})
export class RequestsPageComponent {
  protected readonly store = inject(RequestsListStore);
  private readonly user = inject(CurrentUser);

  protected readonly pageSizes = PAGE_SIZES;
  protected readonly outcomeLabels = BULK_OUTCOME_LABELS;
  protected readonly filters = computed(() => filtersOf(this.store.query()), { equal: sameValue });
  protected readonly summaryRefresh = signal(0);
  protected readonly bulkBusy = signal(false);
  protected readonly bulkResult = signal<BulkUpdateResult | null>(null);
  protected readonly bulkError = signal<string | null>(null);

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
