import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { PagedResult, RequestListItem } from '../../core/models/request.models';
import { EMPTY_FILTERS } from './query-params';
import { RequestsListStore } from './requests-list.store';

const item = (id: number): RequestListItem => ({
  id,
  title: `פנייה ${id}`,
  organizationName: 'ארגון',
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

/** Lets the router finish a navigation and the store react to it. */
const settle = () => new Promise((resolve) => setTimeout(resolve));

describe('RequestsListStore', () => {
  let http: HttpTestingController;
  let router: Router;

  async function createStore(url = '/'): Promise<RequestsListStore> {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: '**', children: [] }]), RequestsListStore],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    await router.navigateByUrl(url);
    const store = TestBed.inject(RequestsListStore);
    TestBed.tick();
    return store;
  }

  const searchRequests = (): TestRequest[] => http.match((r) => r.url === '/api/requests');

  afterEach(() => http.verify());

  it('builds the first request from the URL', async () => {
    await createStore('/?search=%D7%94%D7%99%D7%AA%D7%A8&status=New&status=Waiting&page=3&sortBy=title&sortDirection=asc');

    const [req] = searchRequests();
    expect(req.request.params.get('search')).toBe('היתר');
    expect(req.request.params.getAll('status')).toEqual(['New', 'Waiting']);
    expect(req.request.params.get('page')).toBe('3');
    expect(req.request.params.get('sortBy')).toBe('title');
    expect(req.request.params.get('sortDirection')).toBe('asc');
    req.flush(page([item(1)]));
  });

  it('ignores invalid values in a hand-edited URL', async () => {
    await createStore('/?status=Closed&page=-4&pageSize=5000&sortBy=password');

    const [req] = searchRequests();
    expect(req.request.params.getAll('status')).toBeNull();
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('pageSize')).toBe('20');
    expect(req.request.params.get('sortBy')).toBe('createdAt');
    req.flush(page([]));
  });

  it('writes filters to the URL, goes back to page 1 and cancels the in-flight search', async () => {
    const store = await createStore('/?page=4');
    const [first] = searchRequests();

    store.setFilters({ ...EMPTY_FILTERS, search: 'דחוף', priority: ['High'] });
    await settle();

    expect(first.cancelled).toBe(true);
    expect(router.url).toBe('/?search=%D7%93%D7%97%D7%95%D7%A3&priority=High');
    const [second] = searchRequests();
    expect(second.request.params.get('page')).toBe('1');
    second.flush(page([item(2)]));
    expect(store.result()?.items.map((i) => i.id)).toEqual([2]);
  });

  it('opening a request puts its id in the URL without reloading the list', async () => {
    const store = await createStore('/?status=New');
    searchRequests()[0].flush(page([item(7)]));

    store.openRequest(7);
    await settle();

    expect(router.url).toBe('/?status=New&id=7');
    expect(store.selectedId()).toBe(7);
    expect(searchRequests()).toHaveLength(0);
  });

  it('clears the selection when filters change', async () => {
    const store = await createStore();
    searchRequests()[0].flush(page([item(1), item(2)]));
    store.toggleSelection(item(1));
    expect(store.selection().size).toBe(1);

    store.setFilters({ ...EMPTY_FILTERS, priority: ['High'] });
    await settle();

    expect(store.selection().size).toBe(0);
    searchRequests()[0].flush(page([]));
    expect(store.isEmpty()).toBe(true);
  });

  it('exposes an error state with the server message', async () => {
    const store = await createStore();

    searchRequests()[0].flush(
      { title: 'One or more validation errors occurred.', errors: { PageSize: ['Too big.'] } },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(store.loading()).toBe(false);
    expect(store.error()).toBe('Too big.');
  });
});
