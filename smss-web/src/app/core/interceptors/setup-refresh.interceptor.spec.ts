import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../testing/auth-test-utils';
import { envelope, makeStatus } from '../testing/onboarding-test-utils';
import { OnboardingService } from '../services/onboarding.service';
import { errorInterceptor } from './error.interceptor';
import { isSetupApiWrite, setupRefreshInterceptor } from './setup-refresh.interceptor';

const API = environment.apiUrl;
const STATUS = `${API}/api/Onboarding/status`;

describe('setupRefreshInterceptor', () => {
  let http: HttpTestingController;
  let client: HttpClient;

  function setup(roles = ['School Admin']) {
    clearSessions();
    seedSession({ user: makeUser({ roles }) });
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([errorInterceptor, setupRefreshInterceptor])), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
  }

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it.each([['/api/AcademicYear'], ['/api/AcademicYear/5/toggle-status'], ['/api/Classes'], ['/api/Sections/3'], ['/api/MasterImport/import'], ['/api/FeeType'], ['/api/FeeStructure/4']])(
    'reads the status again after a successful POST to %s',
    (path) => {
      setup();
      client.post(`${API}${path}`, {}).subscribe();
      http.expectOne(`${API}${path}`).flush(envelope({}));
      http.expectOne(STATUS).flush(envelope(makeStatus(3)));
      expect(TestBed.inject(OnboardingService).status()?.nextStep?.code).toBe('SCHOOL_SETUP');
    },
  );

  it('also after a PUT', () => {
    setup();
    client.put(`${API}/api/FeeStructure/4`, {}).subscribe();
    http.expectOne(`${API}/api/FeeStructure/4`).flush(envelope({}));
    http.expectOne(STATUS).flush(envelope(makeStatus(5)));
  });

  it('does nothing after a failed save', () => {
    setup();
    client.post(`${API}/api/Classes`, {}).subscribe({ error: () => undefined });
    http.expectOne(`${API}/api/Classes`).flush({ message: 'dup' }, { status: 409, statusText: 'Conflict' });
    http.expectNone(STATUS);
  });

  it('does nothing after a read or an unrelated save', () => {
    setup();
    client.get(`${API}/api/Classes`).subscribe();
    http.expectOne(`${API}/api/Classes`).flush(envelope([]));
    client.post(`${API}/api/Notice`, {}).subscribe();
    http.expectOne(`${API}/api/Notice`).flush(envelope({}));
    http.expectNone(STATUS);
  });

  it('asks nothing for a role that has no status', () => {
    setup(['Teacher']);
    client.post(`${API}/api/Classes`, {}).subscribe();
    http.expectOne(`${API}/api/Classes`).flush(envelope({}));
    http.expectNone(STATUS);
  });

  it('matches whole path segments only', () => {
    expect(isSetupApiWrite('POST', `${API}/api/ClassesX`)).toBe(false);
    expect(isSetupApiWrite('POST', `${API}/api/Classes`)).toBe(true);
    expect(isSetupApiWrite('GET', `${API}/api/Classes`)).toBe(false);
  });

  it('shows the friendly dialog, not the raw error toast, for a 409 SETUP_INCOMPLETE', () => {
    setup();
    client.post(`${API}/api/Employee`, {}).subscribe({ error: () => undefined });
    http
      .expectOne(`${API}/api/Employee`)
      .flush(
        { status: false, statusCode: 409, message: 'Finish the school setup first.', data: { code: 'SETUP_INCOMPLETE', nextStep: { code: 'ACADEMIC_YEAR', title: 'Add the academic year', route: '/school-setup/academic-year', done: false, detail: '' } } },
        { status: 409, statusText: 'Conflict' },
      );
    http.expectOne(STATUS).flush(envelope(makeStatus(1)));
    // the dialog is open (a promise-based host), and no error toast was raised
    expect(TestBed.inject(ConfirmStateProbe).open()).toBe(true);
  });
});

import { Injectable, inject } from '@angular/core';
import { ConfirmDialogService } from '../services/confirm-dialog.service';
import { ToastService } from '../services/toast.service';

@Injectable({ providedIn: 'root' })
class ConfirmStateProbe {
  private readonly confirm = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  /** A confirm dialog is showing and no error toast is. */
  open(): boolean {
    return !!this.confirm.state() && this.toast.toasts().length === 0;
  }
}
