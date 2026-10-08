import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment.development';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { ApiService } from './api.service';
import { ToastService } from './toast.service';

describe('ApiService + errorInterceptor', () => {
  let api: ApiService;
  let http: HttpTestingController;
  let toast: ToastService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([errorInterceptor])), provideHttpClientTesting()],
    });
    api = TestBed.inject(ApiService);
    http = TestBed.inject(HttpTestingController);
    toast = TestBed.inject(ToastService);
  });

  it('prefixes the base url and unwraps data', () => {
    let result: unknown;
    api.get<{ id: number }>('/api/MasterData/board-types').subscribe((r) => (result = r));
    const req = http.expectOne(`${environment.apiUrl}/api/MasterData/board-types`);
    req.flush({ status: true, statusCode: 200, message: 'ok', data: { id: 1 } });
    expect(result).toEqual({ id: 1 });
  });

  it('errors when the envelope status is false', () => {
    let error: unknown;
    api.get('/x').subscribe({ error: (e) => (error = e) });
    http.expectOne(`${environment.apiUrl}/x`).flush({ status: false, statusCode: 200, message: 'nope', data: null });
    expect(error).toMatchObject({ message: 'nope' });
  });

  it('toasts the server message on HTTP errors and re-throws', () => {
    let failed = false;
    api.post('/x').subscribe({ error: () => (failed = true) });
    http.expectOne(`${environment.apiUrl}/x`).flush({ status: false, statusCode: 400, message: 'School code exists', data: null }, { status: 400, statusText: 'Bad Request' });
    expect(failed).toBe(true);
    expect(toast.toasts()[0]).toMatchObject({ type: 'error', message: 'School code exists' });
  });

  it('shows the field messages of a "Validation failed" response instead of the generic text', () => {
    api.put('/x', {}).subscribe({ error: () => undefined });
    http.expectOne(`${environment.apiUrl}/x`).flush(
      { status: false, statusCode: 400, message: 'Validation failed', data: { MobileNumber: ['Enter a valid 10-digit mobile number.'], Email: ['The Email field is not a valid e-mail address.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    expect(toast.toasts()[0].message).toBe('Enter a valid 10-digit mobile number. The Email field is not a valid e-mail address.');
  });

  it('falls back to a friendly message when the server is unreachable', () => {
    api.get('/x').subscribe({ error: () => undefined });
    http.expectOne(`${environment.apiUrl}/x`).error(new ProgressEvent('error'), { status: 0 });
    expect(toast.toasts()[0].message).toContain('Cannot reach the server');
  });
});
