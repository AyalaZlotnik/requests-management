import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ConnectionStatus, connectionInterceptor } from './connection-status.service';

describe('connectionInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let status: ConnectionStatus;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([connectionInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    status = TestBed.inject(ConnectionStatus);
  });

  afterEach(() => controller.verify());

  it('goes offline when the server cannot be reached and back online on the next response', () => {
    http.get('/api/requests').subscribe({ error: () => undefined });
    controller.expectOne('/api/requests').error(new ProgressEvent('error'), { status: 0 });
    expect(status.offline()).toBe(true);

    http.get('/api/requests').subscribe();
    // Still offline while the request is in flight – no flicker.
    expect(status.offline()).toBe(true);
    controller.expectOne('/api/requests').flush({ items: [] });
    expect(status.offline()).toBe(false);
  });

  it('an error answered by the API itself means the server is reachable', () => {
    status.offline.set(true);

    http.get('/api/requests/9').subscribe({ error: () => undefined });
    controller.expectOne('/api/requests/9').flush({ title: 'Resource not found' }, { status: 404, statusText: 'Not Found' });

    expect(status.offline()).toBe(false);
  });
});
