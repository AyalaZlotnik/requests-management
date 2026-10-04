import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  BulkStatusItem,
  BulkUpdateResult,
  PagedResult,
  RequestDetails,
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

  getSummary(): Observable<RequestsSummary> {
    return this.http.get<RequestsSummary>(`${this.baseUrl}/summary`);
  }

  getById(id: number): Observable<RequestDetails> {
    return this.http.get<RequestDetails>(`${this.baseUrl}/${id}`);
  }

  getHistory(id: number): Observable<StatusHistoryEntry[]> {
    return this.http.get<StatusHistoryEntry[]>(`${this.baseUrl}/${id}/history`);
  }

  updateStatus(id: number, status: RequestStatus, rowVersion: string, changedBy: string): Observable<RequestDetails> {
    return this.http.patch<RequestDetails>(`${this.baseUrl}/${id}/status`, { status, rowVersion, changedBy });
  }

  bulkUpdateStatus(status: RequestStatus, items: BulkStatusItem[], changedBy: string): Observable<BulkUpdateResult> {
    return this.http.post<BulkUpdateResult>(`${this.baseUrl}/bulk/status`, { status, items, changedBy });
  }
}

function toParams(query: RequestQuery): HttpParams {
  let params = new HttpParams()
    .set('page', query.page)
    .set('pageSize', query.pageSize)
    .set('sortBy', query.sortBy)
    .set('sortDirection', query.sortDirection);

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

  // Dates are picked as calendar days; the API filters on UTC timestamps (inclusive range).
  if (query.createdFrom) params = params.set('createdFrom', `${query.createdFrom}T00:00:00Z`);
  if (query.createdTo) params = params.set('createdTo', `${query.createdTo}T23:59:59.999Z`);

  return params;
}
