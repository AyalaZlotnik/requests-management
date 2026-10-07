import { HttpClient, HttpHeaders, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import {
  BulkStatusItem,
  BulkUpdateResult,
  PagedResult,
  RequestDetails,
  RequestFilters,
  RequestListItem,
  RequestQuery,
  RequestsSummary,
  RequestStatus,
  StatusHistoryEntry,
} from '../models/request.models';

/** Thin HTTP layer – no state, one method per endpoint. */
@Injectable({ providedIn: 'root' })
export class RequestsApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/requests';

  search(query: RequestQuery): Observable<PagedResult<RequestListItem>> {
    return this.http.get<PagedResult<RequestListItem>>(this.baseUrl, { params: toParams(query) });
  }

  /** Same filters as the list. */
  getSummary(filters: RequestFilters): Observable<RequestsSummary> {
    return this.http.get<RequestsSummary>(`${this.baseUrl}/summary`, { params: filterParams(filters) });
  }

  /** The ETag is the version to send back in If-Match when updating this request. */
  getById(id: number): Observable<VersionedRequest> {
    return this.http
      .get<RequestDetails>(`${this.baseUrl}/${id}`, { observe: 'response' })
      .pipe(map(toVersioned));
  }

  getHistory(id: number): Observable<StatusHistoryEntry[]> {
    return this.http.get<StatusHistoryEntry[]>(`${this.baseUrl}/${id}/history`);
  }

  /** Conditional update: rejected with 409 if the request changed since the given ETag was read. */
  updateStatus(id: number, status: RequestStatus, etag: string, changedBy: string): Observable<VersionedRequest> {
    return this.http
      .patch<RequestDetails>(
        `${this.baseUrl}/${id}/status`,
        { status, changedBy },
        { headers: new HttpHeaders({ 'If-Match': etag }), observe: 'response' },
      )
      .pipe(map(toVersioned));
  }

  bulkUpdateStatus(status: RequestStatus, items: BulkStatusItem[], changedBy: string): Observable<BulkUpdateResult> {
    return this.http.post<BulkUpdateResult>(`${this.baseUrl}/bulk/status`, { status, items, changedBy });
  }
}

export interface VersionedRequest {
  request: RequestDetails;
  etag: string;
}

/** ETag for a version we already hold (e.g. the current state returned in a 409). */
export function etagOf(rowVersion: string): string {
  return `"${rowVersion}"`;
}

function toVersioned(response: HttpResponse<RequestDetails>): VersionedRequest {
  const request = response.body!;
  return { request, etag: response.headers.get('ETag') ?? etagOf(request.rowVersion) };
}

function toParams(query: RequestQuery): HttpParams {
  return filterParams(query)
    .set('page', query.page)
    .set('pageSize', query.pageSize)
    .set('sortBy', query.sortBy)
    .set('sortDirection', query.sortDirection);
}

function filterParams(query: RequestFilters): HttpParams {
  let params = new HttpParams();
  const text: [string, string][] = [
    ['search', query.search],
    ['organizationName', query.organizationName],
    ['assignedTo', query.assignedTo],
  ];
  for (const [key, value] of text) {
    if (value.trim()) params = params.set(key, value.trim());
  }

  query.status.forEach((s) => (params = params.append('status', s)));
  query.priority.forEach((p) => (params = params.append('priority', p)));

  // Dates are picked as calendar days in the user's time zone. The API filters on UTC timestamps (inclusive range),
  // so each day is sent as local midnight to local end of day, converted to UTC (an Israeli day starts at 21:00 or
  // 22:00 UTC the day before, depending on daylight saving time).
  if (query.createdFrom) params = params.set('createdFrom', localDayStart(query.createdFrom).toISOString());
  if (query.createdTo) params = params.set('createdTo', new Date(localDayStart(query.createdTo, 1).getTime() - 1).toISOString());

  return params;
}

/** Local midnight of a yyyy-MM-dd day, optionally some days later. */
function localDayStart(day: string, addDays = 0): Date {
  const [year, month, date] = day.split('-').map(Number);
  return new Date(year, month - 1, date + addDays);
}
