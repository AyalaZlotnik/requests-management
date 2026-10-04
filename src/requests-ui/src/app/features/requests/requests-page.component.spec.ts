import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DateAdapter, provideNativeDateAdapter } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { HebrewDateAdapter } from '../../core/i18n/hebrew-date-adapter';
import { BulkUpdateResult, RequestDetails, RequestListItem, RequestsSummary } from '../../core/models/request.models';
import { ConflictChoice } from './conflict-dialog.component';
import { RequestsPageComponent } from './requests-page.component';

const item = (id: number, overrides: Partial<RequestListItem> = {}): RequestListItem => ({
  id,
  title: `פנייה ${id}`,
  organizationName: 'ארגון',
  status: 'New',
  priority: 'Medium',
  assignedTo: null,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  rowVersion: `AAAAAAAAAA${id}=`,
  ...overrides,
});

const details = (id: number, overrides: Partial<RequestDetails> = {}): RequestDetails => ({
  ...item(id),
  allowedNextStatuses: ['InProgress', 'Waiting'],
  ...overrides,
});

const page = (items: RequestListItem[]) => ({ items, page: 1, pageSize: 25, totalCount: items.length, totalPages: 1 });

const summary: RequestsSummary = {
  total: 3,
  byStatus: [{ key: 'New', count: 3 }],
  byPriority: [{ key: 'Medium', count: 3 }],
  openOlderThan7Days: 0,
  lastUpdatedAt: '2026-01-01T00:00:00Z',
  topAssignees: [],
  generatedAt: '2026-01-01T00:00:00Z',
};

/** The page with its real children (table, summary, filters, bulk bar, details); only HTTP and the dialog are fake. */
describe('RequestsPageComponent', () => {
  let http: HttpTestingController;
  let dialogChoice: ConflictChoice;

  beforeEach(() => {
    dialogChoice = 'reload';
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: '', component: RequestsPageComponent }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNativeDateAdapter(),
        { provide: DateAdapter, useClass: HebrewDateAdapter },
        { provide: MatDialog, useValue: { open: () => ({ afterClosed: () => of(dialogChoice) }) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const listRequests = () => http.match((r) => r.method === 'GET' && r.url === '/api/requests');
  const summaryRequests = () => http.match((r) => r.url === '/api/requests/summary');

  async function open(url: string, items = [item(1), item(2), item(3)]) {
    const harness = await RouterTestingHarness.create(url);
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/requests').flush(page(items));
    http.expectOne((r) => r.url === '/api/requests/summary').flush(summary);
    harness.detectChanges();
    return harness;
  }

  function button(root: HTMLElement, text: string): HTMLButtonElement {
    return [...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === text) as HTMLButtonElement;
  }

  function flushReloads(harness: RouterTestingHarness, items: RequestListItem[]) {
    // The summary re-fetches through toObservable(refreshKey), which runs during change detection.
    harness.detectChanges();
    const lists = listRequests();
    const summaries = summaryRequests();
    expect(lists).toHaveLength(1);
    expect(summaries).toHaveLength(1);
    lists[0].flush(page(items));
    summaries[0].flush(summary);
    harness.detectChanges();
  }

  it('bulk update with mixed outcomes: Hebrew summary, a link per failed request, selection cleared, list and summary reloaded', async () => {
    const harness = await open('/');
    const root = harness.routeNativeElement!;

    // Select all three rows (the first checkbox is "select all on page").
    const rowBoxes = [...root.querySelectorAll<HTMLInputElement>('input[type=checkbox]')].slice(1);
    rowBoxes.forEach((box) => box.click());
    harness.detectChanges();
    expect(root.querySelector('.bulk-bar')?.textContent).toContain('3 פניות נבחרו');

    button(root, 'עדכון כל הנבחרות').click();
    const post: TestRequest = http.expectOne('/api/requests/bulk/status');
    // Each item carries the version the user saw, so the server can detect changes made meanwhile.
    expect(post.request.body.status).toBe('InProgress');
    expect(post.request.body.items).toEqual([
      { id: 1, rowVersion: 'AAAAAAAAAA1=' },
      { id: 2, rowVersion: 'AAAAAAAAAA2=' },
      { id: 3, rowVersion: 'AAAAAAAAAA3=' },
    ]);
    const result: BulkUpdateResult = {
      requested: 3,
      succeeded: 1,
      failed: 2,
      results: [
        { id: 1, outcome: 'Updated', error: null, rowVersion: 'AAAAAAAAAA9=' },
        { id: 2, outcome: 'Conflict', error: 'changed', rowVersion: null },
        { id: 3, outcome: 'NotFound', error: 'missing', rowVersion: null },
      ],
    };
    post.flush(result);
    flushReloads(harness, [item(1, { status: 'InProgress' }), item(2, { status: 'Waiting' })]);

    const notice = root.querySelector('.notice') as HTMLElement;
    expect(notice.classList).toContain('warning');
    expect(notice.textContent).toContain('עדכון מרוכז: עודכנה פנייה אחת; לא עודכנו 2 פניות.');
    const failed = [...notice.querySelectorAll('li')].map((li) => li.textContent?.replace(/\s+/g, ' ').trim());
    expect(failed).toEqual(['פנייה #2 – עודכנה בינתיים על ידי משתמש אחר', 'פנייה #3 – הפנייה לא נמצאה']);
    expect(root.querySelector('.bulk-bar')).toBeNull();

    // The link opens that request in the details panel; only "id" changes, so the list is not fetched again.
    notice.querySelector('a')!.click();
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toContain('id=2');
    http.expectOne('/api/requests/2').flush(details(2, { status: 'Waiting' }));
    http.expectOne('/api/requests/2/history').flush([]);
    expect(listRequests()).toHaveLength(0);
  });

  it('after a successful status update the list and the summary are reloaded', async () => {
    const harness = await open('/?id=7', [item(7)]);
    http.expectOne('/api/requests/7').flush(details(7), { headers: { ETag: '"AAAAAAAAAA7="' } });
    http.expectOne('/api/requests/7/history').flush([]);
    harness.detectChanges();

    button(harness.routeNativeElement!.querySelector('app-request-details')!, 'עדכון').click();
    http.expectOne('/api/requests/7/status').flush(details(7, { status: 'InProgress' }), { headers: { ETag: '"AAAAAAAAAA8="' } });
    http.expectOne('/api/requests/7').flush(details(7, { status: 'InProgress' }));
    http.expectOne('/api/requests/7/history').flush([]);

    flushReloads(harness, [item(7, { status: 'InProgress' })]);
    expect(harness.routeNativeElement!.querySelector('app-requests-table')?.textContent).toContain('בטיפול');
  });

  it('after a 409 the list and the summary are reloaded too, so they show the state that won', async () => {
    const harness = await open('/?id=7', [item(7)]);
    http.expectOne('/api/requests/7').flush(details(7), { headers: { ETag: '"AAAAAAAAAA7="' } });
    http.expectOne('/api/requests/7/history').flush([]);
    harness.detectChanges();

    button(harness.routeNativeElement!.querySelector('app-request-details')!, 'עדכון').click();
    const current = details(7, { status: 'Completed', rowVersion: 'AAAAAAAAAB0=', allowedNextStatuses: ['InProgress'] });
    http.expectOne('/api/requests/7/status').flush({ currentState: current, lastChange: null }, { status: 409, statusText: 'Conflict' });
    http.expectOne('/api/requests/7').flush(current);
    http.expectOne('/api/requests/7/history').flush([]);

    flushReloads(harness, [item(7, { status: 'Completed' })]);
    expect(harness.routeNativeElement!.querySelector('app-requests-table')?.textContent).toContain('הושלמה');
    // The user chose to look at the current state, so nothing else was sent.
    http.expectNone('/api/requests/7/status');
  });
});
