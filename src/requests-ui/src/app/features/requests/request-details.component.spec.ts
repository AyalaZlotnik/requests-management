import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { RequestDetails, StatusHistoryEntry } from '../../core/models/request.models';
import { ConflictChoice, ConflictDialogData } from './conflict-dialog.component';
import { RequestDetailsComponent } from './request-details.component';

const request = (overrides: Partial<RequestDetails> = {}): RequestDetails => ({
  id: 7,
  title: 'חידוש היתר',
  organizationName: 'ארגון',
  status: 'New',
  priority: 'High',
  assignedTo: null,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  rowVersion: 'AAAAAAAAAAE=',
  allowedNextStatuses: ['InProgress', 'Waiting'],
  ...overrides,
});

const lastChange: StatusHistoryEntry = {
  id: 1,
  previousStatus: 'New',
  newStatus: 'Waiting',
  changedAt: '2026-01-02T10:00:00Z',
  changedBy: 'יוסי כהן',
};

describe('RequestDetailsComponent', () => {
  let http: HttpTestingController;
  let dialogData: ConflictDialogData | undefined;
  let dialogChoice: ConflictChoice;
  /** Every conflict dialog opened, in order; and answers to give, in order (falls back to dialogChoice). */
  let dialogsOpened: ConflictDialogData[];
  let nextChoices: ConflictChoice[];

  beforeEach(() => {
    dialogData = undefined;
    dialogChoice = 'retry';
    dialogsOpened = [];
    nextChoices = [];
    TestBed.configureTestingModule({
      imports: [RequestDetailsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: MatDialog,
          useValue: {
            open: (_: unknown, config: { data: ConflictDialogData }) => {
              dialogData = config.data;
              dialogsOpened.push(config.data);
              return { afterClosed: () => of(nextChoices.shift() ?? dialogChoice) };
            },
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function render() {
    const fixture = TestBed.createComponent(RequestDetailsComponent);
    fixture.componentRef.setInput('requestId', 7);
    fixture.detectChanges();
    http.expectOne('/api/requests/7').flush(request(), { headers: { ETag: '"AAAAAAAAAAE="' } });
    http.expectOne('/api/requests/7/history').flush([]);
    fixture.detectChanges();
    return fixture;
  }

  function clickUpdate(fixture: ReturnType<typeof render>) {
    const button = [...fixture.nativeElement.querySelectorAll('button')].find((b: HTMLButtonElement) =>
      b.textContent?.includes('עדכון'),
    ) as HTMLButtonElement;
    button.click();
  }

  it('sends the ETag it read as If-Match', () => {
    const fixture = render();

    clickUpdate(fixture);

    const patch = http.expectOne('/api/requests/7/status');
    expect(patch.request.headers.get('If-Match')).toBe('"AAAAAAAAAAE="');
    expect(patch.request.body.status).toBe('InProgress');
    patch.flush(request({ status: 'InProgress' }), { headers: { ETag: '"AAAAAAAAAAI="' } });
    // After a successful update the panel reloads the request and its history.
    http.expectOne('/api/requests/7').flush(request({ status: 'InProgress' }));
    http.expectOne('/api/requests/7/history').flush([]);
  });

  it('on 409 shows who changed it, and "retry" sends the update on top of the current version', () => {
    const fixture = render();
    const current = request({ status: 'Waiting', rowVersion: 'AAAAAAAAAAM=', allowedNextStatuses: ['InProgress', 'Completed'] });

    clickUpdate(fixture);
    http
      .expectOne('/api/requests/7/status')
      .flush({ title: 'Concurrency conflict', currentState: current, lastChange }, { status: 409, statusText: 'Conflict' });

    expect(dialogData?.attempted).toBe('InProgress');
    expect(dialogData?.current.status).toBe('Waiting');
    expect(dialogData?.lastChange?.changedBy).toBe('יוסי כהן');

    // The panel reloads what is stored now, then the retry goes out with the version from the 409 body.
    http.expectOne('/api/requests/7').flush(current);
    http.expectOne('/api/requests/7/history').flush([lastChange]);
    const retry = http.expectOne('/api/requests/7/status');
    expect(retry.request.headers.get('If-Match')).toBe('"AAAAAAAAAAM="');
    expect(retry.request.body.status).toBe('InProgress');
    retry.flush(request({ status: 'InProgress' }));
    http.expectOne('/api/requests/7').flush(request({ status: 'InProgress' }));
    http.expectOne('/api/requests/7/history').flush([]);
  });

  it('a retry that hits another 409 opens the dialog again with the newest state and sends nothing twice', () => {
    const fixture = render();
    const afterFirst = request({ status: 'Waiting', rowVersion: 'AAAAAAAAAAM=', allowedNextStatuses: ['InProgress', 'Completed'] });
    const afterSecond = request({ status: 'Completed', rowVersion: 'AAAAAAAAAAQ=', allowedNextStatuses: ['InProgress'] });
    const secondChange: StatusHistoryEntry = { ...lastChange, id: 2, previousStatus: 'Waiting', newStatus: 'Completed', changedBy: 'מיכל פרץ' };
    nextChoices = ['retry', 'reload'];

    clickUpdate(fixture);
    http.expectOne('/api/requests/7/status').flush({ currentState: afterFirst, lastChange }, { status: 409, statusText: 'Conflict' });
    http.expectOne('/api/requests/7').flush(afterFirst);
    http.expectOne('/api/requests/7/history').flush([lastChange]);

    // The retry is sent on top of the version from the first 409 – and a third user got there first again.
    const retry = http.expectOne('/api/requests/7/status');
    expect(retry.request.headers.get('If-Match')).toBe('"AAAAAAAAAAM="');
    retry.flush({ currentState: afterSecond, lastChange: secondChange }, { status: 409, statusText: 'Conflict' });
    http.expectOne('/api/requests/7').flush(afterSecond);
    http.expectOne('/api/requests/7/history').flush([secondChange, lastChange]);

    // Second dialog shows the newest state and who changed it; the user chooses to look, so nothing more is sent.
    expect(dialogsOpened).toHaveLength(2);
    expect(dialogsOpened[1].current.status).toBe('Completed');
    expect(dialogsOpened[1].lastChange?.changedBy).toBe('מיכל פרץ');
    http.expectNone('/api/requests/7/status');
  });

  it('on 409 with "show current state" sends nothing else', () => {
    dialogChoice = 'reload';
    const fixture = render();

    clickUpdate(fixture);
    http
      .expectOne('/api/requests/7/status')
      .flush({ currentState: request({ status: 'Waiting' }), lastChange }, { status: 409, statusText: 'Conflict' });

    http.expectOne('/api/requests/7').flush(request({ status: 'Waiting' }));
    http.expectOne('/api/requests/7/history').flush([lastChange]);
    http.expectNone('/api/requests/7/status');
  });
});
