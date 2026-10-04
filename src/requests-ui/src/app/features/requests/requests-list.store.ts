import { Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Observable, Subject, catchError, map, merge, of, switchMap, tap } from 'rxjs';
import { RequestsApi } from '../../core/api/requests-api.service';
import { errorMessage } from '../../core/api/http-error';
import {
  BulkUpdateResult,
  PagedResult,
  RequestFilters,
  RequestListItem,
  RequestQuery,
  RequestStatus,
  SortField,
} from '../../core/models/request.models';

export const EMPTY_FILTERS: RequestFilters = {
  search: '',
  status: [],
  priority: [],
  organizationName: '',
  assignedTo: '',
  createdFrom: '',
  createdTo: '',
};

const INITIAL_QUERY: RequestQuery = {
  ...EMPTY_FILTERS,
  page: 1,
  pageSize: 20,
  sortBy: 'createdAt',
  sortDirection: 'desc',
};

type SearchOutcome = { ok: true; result: PagedResult<RequestListItem> } | { ok: false; error: string };

/**
 * State of the requests list. Provided per page component.
 * Every query change goes through switchMap, so an in-flight HTTP request is cancelled when a newer
 * query arrives (fast typing / paging) and only the latest response is ever shown.
 * All filtering, sorting and paging happen on the server – the client only holds one page.
 */
@Injectable()
export class RequestsListStore {
  private readonly api = inject(RequestsApi);
  private readonly reload$ = new Subject<void>();

  readonly query = signal<RequestQuery>(INITIAL_QUERY);

  private readonly _result = signal<PagedResult<RequestListItem> | null>(null);
  private readonly _loading = signal(true);
  private readonly _error = signal<string | null>(null);
  /** Selected rows for bulk update: id → rowVersion read by the user. */
  private readonly _selection = signal<ReadonlyMap<number, string>>(new Map());

  readonly result = this._result.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly selection = this._selection.asReadonly();
  readonly selectedIds = computed(() => new Set(this._selection().keys()));
  readonly isEmpty = computed(() => !this._loading() && !this._error() && this._result()?.totalCount === 0);

  constructor() {
    merge(toObservable(this.query), this.reload$.pipe(map(() => this.query())))
      .pipe(
        tap(() => {
          this._loading.set(true);
          this._error.set(null);
        }),
        switchMap((query) =>
          this.api.search(query).pipe(
            map((result): SearchOutcome => ({ ok: true, result })),
            catchError((error) => of<SearchOutcome>({ ok: false, error: errorMessage(error) })),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((outcome) => {
        this._loading.set(false);
        if (outcome.ok) {
          this._result.set(outcome.result);
        } else {
          this._error.set(outcome.error);
        }
      });
  }

  setFilters(filters: RequestFilters): void {
    this.query.update((q) => ({ ...q, ...filters, page: 1 }));
    this.clearSelection();
  }

  sortBy(field: SortField): void {
    this.query.update((q) => ({
      ...q,
      sortBy: field,
      sortDirection: q.sortBy === field && q.sortDirection === 'desc' ? 'asc' : 'desc',
      page: 1,
    }));
  }

  setPage(page: number): void {
    this.query.update((q) => ({ ...q, page }));
  }

  setPageSize(pageSize: number): void {
    this.query.update((q) => ({ ...q, pageSize, page: 1 }));
  }

  reload(): void {
    this.reload$.next();
  }

  toggleSelection(item: RequestListItem): void {
    this._selection.update((current) => {
      const next = new Map(current);
      if (next.has(item.id)) next.delete(item.id);
      else next.set(item.id, item.rowVersion);
      return next;
    });
  }

  toggleAllOnPage(): void {
    const items = this._result()?.items ?? [];
    const allSelected = items.length > 0 && items.every((i) => this._selection().has(i.id));
    this._selection.update((current) => {
      const next = new Map(current);
      items.forEach((i) => (allSelected ? next.delete(i.id) : next.set(i.id, i.rowVersion)));
      return next;
    });
  }

  clearSelection(): void {
    this._selection.set(new Map());
  }

  bulkUpdate(status: RequestStatus, changedBy: string): Observable<BulkUpdateResult> {
    const items = [...this._selection()].map(([id, rowVersion]) => ({ id, rowVersion }));
    return this.api.bulkUpdateStatus(status, items, changedBy).pipe(
      tap(() => {
        this.clearSelection();
        this.reload();
      }),
    );
  }
}
