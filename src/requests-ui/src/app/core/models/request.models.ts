// Contracts matching the API DTOs (backend/Requests.Api/Features/Requests/Dtos).

export const REQUEST_STATUSES = ['New', 'InProgress', 'Waiting', 'Completed'] as const;
export type RequestStatus = (typeof REQUEST_STATUSES)[number];

export const REQUEST_PRIORITIES = ['Low', 'Medium', 'High'] as const;
export type RequestPriority = (typeof REQUEST_PRIORITIES)[number];

export type SortField = 'createdAt' | 'updatedAt' | 'priority' | 'status' | 'title' | 'organizationName';
export type SortDirection = 'asc' | 'desc';

export interface RequestListItem {
  id: number;
  title: string;
  organizationName: string;
  status: RequestStatus;
  priority: RequestPriority;
  assignedTo: string | null;
  createdAt: string;
  updatedAt: string;
  rowVersion: string;
}

export interface RequestDetails extends RequestListItem {
  allowedNextStatuses: RequestStatus[];
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface StatusHistoryEntry {
  id: number;
  previousStatus: RequestStatus;
  newStatus: RequestStatus;
  changedAt: string;
  changedBy: string;
}

export interface CountByKey<T> {
  key: T;
  count: number;
}

/** Aggregations for the current filter. byStatus ignores the status filter, byPriority the priority filter. */
export interface RequestsSummary {
  total: number;
  byStatus: CountByKey<RequestStatus>[];
  byPriority: CountByKey<RequestPriority>[];
  openOlderThan7Days: number;
  lastUpdatedAt: string | null;
  topAssignees: CountByKey<string>[];
  generatedAt: string;
}

export interface RequestFilters {
  search: string;
  status: RequestStatus[];
  priority: RequestPriority[];
  organizationName: string;
  assignedTo: string;
  /** yyyy-MM-dd (inclusive) */
  createdFrom: string;
  /** yyyy-MM-dd (inclusive) */
  createdTo: string;
}

export interface RequestQuery extends RequestFilters {
  page: number;
  pageSize: number;
  sortBy: SortField;
  sortDirection: SortDirection;
}

export interface BulkStatusItem {
  id: number;
  rowVersion: string;
}

export type BulkItemOutcome = 'Updated' | 'NotFound' | 'Conflict' | 'InvalidTransition';

export interface BulkUpdateResult {
  requested: number;
  succeeded: number;
  failed: number;
  results: { id: number; outcome: BulkItemOutcome; error: string | null; rowVersion: string | null }[];
}
