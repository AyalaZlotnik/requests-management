import { Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { Observable, Subject, catchError, distinctUntilChanged, map, merge, of, switchMap, tap } from 'rxjs';
import { RequestsApi } from '../../core/api/requests-api.service';
import { errorMessage } from '../../core/api/http-error';
import {
  BulkUpdateResult,
  PagedResult,
  RequestFilters,
  RequestListItem,
  RequestQuery,
  RequestStatus,
  SortDirection,
  SortField,
} from '../../core/models/request.models';
import { UrlState, parseUrlState, sameValue, toQueryParams } from './query-params';

type SearchOutcome = { ok: true; result: PagedResult<RequestListItem> } | { ok: false; error: string };

/**
 * State of the requests list, provided per page component.
 * The URL is the single source of truth: every action navigates, and the list is fetched from what the
 * URL says – so refresh, shared links and Back show the same view.
 * Every query change goes through switchMap: an in-flight HTTP request is cancelled when a newer query
 * arrives (fast typing / paging) and only the latest response is ever shown.
 * All filtering, sorting and paging happen on the server – the client only holds one page.
 */
@Injectable()
export class RequestsListStore {
  private readonly api = inject(RequestsApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly reload$ = new Subject<void>();

  private readonly urlState = toSignal(this.route.queryParamMap.pipe(map(parseUrlState)), { requireSync: true });

  readonly query = computed(() => this.urlState().query, { equal: sameValue });
  readonly selectedId = computed(() => this.urlState().selectedId);

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
    // Opening a request changes only "id" in the URL – that must not reload the list.
    const query$ = this.route.queryParamMap.pipe(
      map((params) => parseUrlState(params).query),
      distinctUntilChanged(sameValue),
    );

    merge(query$, this.reload$.pipe(map(() => this.query())))
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
    this.navigate({ ...this.query(), ...filters, page: 1 });
    this.clearSelection();
  }

  sort(sortBy: SortField, sortDirection: SortDirection): void {
    this.navigate({ ...this.query(), sortBy, sortDirection, page: 1 });
  }

  setPage(page: number, pageSize: number): void {
    const q = this.query();
    this.navigate({ ...q, pageSize, page: pageSize === q.pageSize ? page : 1 });
  }

  openRequest(id: number | null): void {
    this.navigate(this.query(), id);
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

  private navigate(query: RequestQuery, selectedId: number | null = this.selectedId()): void {
    const state: UrlState = { query, selectedId };
    void this.router.navigate([], { relativeTo: this.route, queryParams: toQueryParams(state) });
  }
}
