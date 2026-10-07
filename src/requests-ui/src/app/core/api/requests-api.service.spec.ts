import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DEFAULT_QUERY } from '../../features/requests/query-params';
import { RequestsApi } from './requests-api.service';

describe('RequestsApi date filter', () => {
  // The users are in Israel: UTC+3 in summer (daylight saving time), UTC+2 in winter.
  beforeEach(() => vi.stubEnv('TZ', 'Asia/Jerusalem'));
  afterEach(() => vi.unstubAllEnvs());

  function sentParams(createdFrom: string, createdTo: string) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(RequestsApi).search({ ...DEFAULT_QUERY, createdFrom, createdTo }).subscribe();
    const request = http.expectOne((r) => r.url === '/api/requests');
    const params = request.request.params;
    request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    http.verify();
    return { from: params.get('createdFrom'), to: params.get('createdTo') };
  }

  it('sends a calendar day as the local Israeli day, so a request from 01:00 Israel time is included', () => {
    // Oct 5 starts at 21:00 UTC on Oct 4 – a request created at 01:00 Israel time (22:00 UTC) is inside the range.
    expect(sentParams('2026-10-05', '2026-10-05')).toEqual({
      from: '2026-10-04T21:00:00.000Z',
      to: '2026-10-05T20:59:59.999Z',
    });
  });

  it('follows daylight saving time: a winter day starts at 22:00 UTC', () => {
    expect(sentParams('2026-01-15', '2026-01-31')).toEqual({
      from: '2026-01-14T22:00:00.000Z',
      to: '2026-01-31T21:59:59.999Z',
    });
  });
});
