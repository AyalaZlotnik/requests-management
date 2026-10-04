import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PagedResult, RequestListItem } from '../../core/models/request.models';
import { EMPTY_FILTERS, RequestsListStore } from './requests-list.store';

const item = (id: number): RequestListItem => ({
  id,
  title: `Request ${id}`,
  organizationName: 'Acme',
  status: 'New',
  priority: 'High',
  assignedTo: null,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  rowVersion: 'AAAAAAAAB9E=',
});

const page = (items: RequestListItem[], pageNumber = 1): PagedResult<RequestListItem> => ({
  items,
  page: pageNumber,
  pageSize: 20,
  totalCount: items.length,
  totalPages: 1,
});

describe('RequestsListStore', () => {
  let store: RequestsListStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), RequestsListStore],
    });
    store = TestBed.inject(RequestsListStore);
    http = TestBed.inject(HttpTestingController);
    TestBed.tick();
  });

  afterEach(() => http.verify());

  it('loads the first page with server-side paging and sorting parameters', () => {
    const req = http.expectOne((r) => r.url === '/api/requests');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('pageSize')).toBe('20');
    expect(req.request.params.get('sortBy')).toBe('createdAt');
    expect(store.loading()).toBe(true);

    req.flush(page([item(1)]));

    expect(store.loading()).toBe(false);
    expect(store.result()?.items.map((i) => i.id)).toEqual([1]);
  });

  it('cancels the in-flight search when the query changes, so only the latest result is shown', () => {
    const first = http.expectOne((r) => r.url === '/api/requests');

    store.setFilters({ ...EMPTY_FILTERS, search: 'permit', status: ['New', 'Waiting'] });
    TestBed.tick();

    expect(first.cancelled).toBe(true);
    const second = http.expectOne((r) => r.params.get('search') === 'permit');
    expect(second.request.params.getAll('status')).toEqual(['New', 'Waiting']);

    second.flush(page([item(2)]));
    expect(store.result()?.items.map((i) => i.id)).toEqual([2]);
  });

  it('goes back to page 1 and clears the selection when filters change', () => {
    http.expectOne((r) => r.url === '/api/requests').flush(page([item(1), item(2)]));
    store.setPage(3);
    TestBed.tick();
    http.expectOne((r) => r.params.get('page') === '3').flush(page([item(5)], 3));
    store.toggleSelection(item(5));
    expect(store.selection().size).toBe(1);

    store.setFilters({ ...EMPTY_FILTERS, priority: ['High'] });
    TestBed.tick();

    const req = http.expectOne((r) => r.params.get('priority') === 'High');
    expect(req.request.params.get('page')).toBe('1');
    expect(store.selection().size).toBe(0);
    req.flush(page([]));
    expect(store.isEmpty()).toBe(true);
  });

  it('exposes an error state with the server message', () => {
    http
      .expectOne((r) => r.url === '/api/requests')
      .flush({ title: 'One or more validation errors occurred.', errors: { PageSize: ['Too big.'] } }, { status: 400, statusText: 'Bad Request' });

    expect(store.loading()).toBe(false);
    expect(store.error()).toBe('Too big.');
  });
});
