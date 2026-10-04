import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RequestStatus, RequestsSummary } from '../../core/models/request.models';
import { EMPTY_FILTERS } from './query-params';
import { SummaryPanelComponent } from './summary-panel.component';

const summary: RequestsSummary = {
  total: 30,
  byStatus: [
    { key: 'New', count: 10 },
    { key: 'InProgress', count: 8 },
    { key: 'Waiting', count: 2 },
    { key: 'Completed', count: 10 },
  ],
  byPriority: [
    { key: 'Low', count: 10 },
    { key: 'Medium', count: 10 },
    { key: 'High', count: 10 },
  ],
  openOlderThan7Days: 4,
  lastUpdatedAt: '2026-01-01T00:00:00Z',
  topAssignees: [],
  generatedAt: '2026-01-01T00:00:00Z',
};

describe('SummaryPanelComponent', () => {
  function render(status: RequestStatus[] = []) {
    TestBed.configureTestingModule({
      imports: [SummaryPanelComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(SummaryPanelComponent);
    fixture.componentRef.setInput('filters', { ...EMPTY_FILTERS, status });
    const clicked: RequestStatus[] = [];
    fixture.componentInstance.statusClick.subscribe((s) => clicked.push(s));
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/api/requests/summary').flush(summary);
    fixture.detectChanges();
    return { el: fixture.nativeElement as HTMLElement, clicked };
  }

  const statusButtons = (el: HTMLElement) => [...el.querySelectorAll('.status-count')] as HTMLButtonElement[];

  it('shows the total, the open-and-old count and a count per status', () => {
    const { el } = render();

    expect([...el.querySelectorAll('.kpi-value')].map((e) => e.textContent?.trim())).toEqual(['30', '4']);
    expect(statusButtons(el).map((b) => b.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'חדשה 10',
      'בטיפול 8',
      'ממתינה 2',
      'הושלמה 10',
    ]);
  });

  it('clicking a status reports it, and the status filtered on its own is marked as selected', () => {
    const { el, clicked } = render(['Waiting']);

    statusButtons(el)[0].click();

    expect(clicked).toEqual(['New']);
    expect(statusButtons(el).map((b) => b.getAttribute('aria-pressed'))).toEqual(['false', 'false', 'true', 'false']);
  });
});
