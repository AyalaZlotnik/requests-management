import { ParamMap, Params } from '@angular/router';
import {
  REQUEST_PRIORITIES,
  REQUEST_STATUSES,
  RequestFilters,
  RequestPriority,
  RequestQuery,
  RequestStatus,
  SortDirection,
  SortField,
} from '../../core/models/request.models';

/**
 * The list state lives in the URL (?search=…&status=New&page=2&id=17), so refresh, shared links and
 * the Back button restore the same view. Values are validated here – a hand-edited URL can't break the page.
 */

export const PAGE_SIZES = [10, 20, 50, 100] as const;

export const EMPTY_FILTERS: RequestFilters = {
  search: '',
  status: [],
  priority: [],
  organizationName: '',
  assignedTo: '',
  createdFrom: '',
  createdTo: '',
};

export const DEFAULT_QUERY: RequestQuery = {
  ...EMPTY_FILTERS,
  page: 1,
  pageSize: 20,
  sortBy: 'createdAt',
  sortDirection: 'desc',
};

const SORT_FIELDS: readonly SortField[] = ['createdAt', 'updatedAt', 'priority', 'status', 'title', 'organizationName'];
const DATE = /^\d{4}-\d{2}-\d{2}$/;

export interface UrlState {
  query: RequestQuery;
  /** Request open in the details panel. */
  selectedId: number | null;
}

export function parseUrlState(params: ParamMap): UrlState {
  const text = (key: string) => params.get(key)?.trim() ?? '';
  const date = (key: string) => (DATE.test(text(key)) ? text(key) : '');
  const positiveInt = (key: string) => {
    const value = Number(params.get(key));
    return Number.isInteger(value) && value > 0 ? value : null;
  };

  const pageSize = positiveInt('pageSize');
  const sortBy = text('sortBy') as SortField;
  const sortDirection = text('sortDirection') as SortDirection;

  return {
    query: {
      search: text('search'),
      status: params.getAll('status').filter((s): s is RequestStatus => (REQUEST_STATUSES as readonly string[]).includes(s)),
      priority: params.getAll('priority').filter((p): p is RequestPriority => (REQUEST_PRIORITIES as readonly string[]).includes(p)),
      organizationName: text('organizationName'),
      assignedTo: text('assignedTo'),
      createdFrom: date('createdFrom'),
      createdTo: date('createdTo'),
      page: positiveInt('page') ?? DEFAULT_QUERY.page,
      pageSize: pageSize && (PAGE_SIZES as readonly number[]).includes(pageSize) ? pageSize : DEFAULT_QUERY.pageSize,
      sortBy: SORT_FIELDS.includes(sortBy) ? sortBy : DEFAULT_QUERY.sortBy,
      sortDirection: sortDirection === 'asc' || sortDirection === 'desc' ? sortDirection : DEFAULT_QUERY.sortDirection,
    },
    selectedId: positiveInt('id'),
  };
}

/** Builds query params, leaving out defaults so URLs stay short. */
export function toQueryParams({ query, selectedId }: UrlState): Params {
  const params: Params = {};
  const set = (key: keyof RequestQuery, value: string | number | string[]) => {
    const isDefault = Array.isArray(value) ? value.length === 0 : value === '' || value === DEFAULT_QUERY[key];
    if (!isDefault) params[key] = value;
  };

  (Object.keys(DEFAULT_QUERY) as (keyof RequestQuery)[]).forEach((key) => set(key, query[key]));
  if (selectedId !== null) params['id'] = selectedId;
  return params;
}

export function filtersOf(query: RequestQuery): RequestFilters {
  const { search, status, priority, organizationName, assignedTo, createdFrom, createdTo } = query;
  return { search, status, priority, organizationName, assignedTo, createdFrom, createdTo };
}

export function sameValue<T>(a: T, b: T): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}
