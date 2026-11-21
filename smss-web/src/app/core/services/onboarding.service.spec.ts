import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../testing/auth-test-utils';
import { envelope, makeStatus } from '../testing/onboarding-test-utils';
import { ConfirmDialogService } from './confirm-dialog.service';
import { OnboardingService, isSetupIncomplete } from './onboarding.service';
import { ToastService } from './toast.service';

const URL = `${environment.apiUrl}/api/Onboarding/status`;

describe('OnboardingService', () => {
  let http: HttpTestingController;

  function setup(user: Parameters<typeof makeUser>[0]) {
    clearSessions();
    seedSession({ user: makeUser(user) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(OnboardingService);
  }

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('has no status for a role the API refuses, and never asks', () => {
    const svc = setup({ roles: ['Teacher'] });
    let got: unknown = 'unset';
    svc.refresh().subscribe((s) => (got = s));
    expect(got).toBeNull();
    expect(svc.eligible()).toBe(false);
    expect(svc.incomplete()).toBe(false);
    http.expectNone(URL);
  });

  it('does not ask while the temporary password is still in place', () => {
    const svc = setup({ roles: ['School Admin'], isFirstLogin: true });
    svc.refresh().subscribe();
    http.expectNone(URL);
  });

  it.each([['School Admin'], ['Principal'], ['Accountant']])('%s reads the status and is "incomplete" until it is complete', (role) => {
    const svc = setup({ roles: [role] });
    svc.refresh().subscribe();
    http.expectOne(URL).flush(envelope(makeStatus(2)));
    expect(svc.incomplete()).toBe(true);
    expect(svc.nextStep()?.code).toBe('IMPORT_MASTERS_OR_CLASSES');
    expect(svc.isLocked('/students')).toBe(true);
    expect(svc.isLocked('/employees/new')).toBe(true);
    expect(svc.isLocked('/transport/vehicles')).toBe(true);
    expect(svc.isLocked('/school-setup/classes')).toBe(false);
    expect(svc.isLocked('/fee-setup/fee-types')).toBe(false);
    expect(svc.isLocked('/dashboard')).toBe(false);
  });

  it('unlocks as soon as the status becomes complete', () => {
    const svc = setup({ roles: ['School Admin'] });
    svc.refresh().subscribe();
    http.expectOne(URL).flush(envelope(makeStatus(5)));
    expect(svc.isLocked('/students')).toBe(true);
    svc.refresh().subscribe();
    http.expectOne(URL).flush(envelope(makeStatus(6)));
    expect(svc.isLocked('/students')).toBe(false);
    expect(svc.nextStep()).toBeNull();
  });

  it('never locks anything while the status is unknown or the request failed', () => {
    const svc = setup({ roles: ['School Admin'] });
    expect(svc.isLocked('/students')).toBe(false);
    svc.refresh().subscribe();
    http.expectOne(URL).flush({ message: 'x' }, { status: 500, statusText: 'err' });
    expect(svc.isLocked('/students')).toBe(false);
  });

  it('keeps the last known answer when a later refresh fails', () => {
    const svc = setup({ roles: ['School Admin'] });
    svc.refresh().subscribe();
    http.expectOne(URL).flush(envelope(makeStatus(1)));
    svc.refresh().subscribe();
    http.expectOne(URL).flush({}, { status: 500, statusText: 'err' });
    expect(svc.incomplete()).toBe(true);
  });

  it('shares one request between concurrent refreshes', () => {
    const svc = setup({ roles: ['School Admin'] });
    svc.refresh().subscribe();
    svc.refresh().subscribe();
    http.expectOne(URL).flush(envelope(makeStatus(1)));
  });

  it('ensure() trusts a known-complete status without asking again', () => {
    const svc = setup({ roles: ['School Admin'] });
    svc.ensure().subscribe();
    http.expectOne(URL).flush(envelope(makeStatus(6)));
    svc.ensure().subscribe();
    http.expectNone(URL);
  });

  it('ensure() asks again while the known status is incomplete', () => {
    const svc = setup({ roles: ['School Admin'] });
    svc.ensure().subscribe();
    http.expectOne(URL).flush(envelope(makeStatus(2)));
    svc.ensure().subscribe();
    http.expectOne(URL).flush(envelope(makeStatus(3)));
    expect(svc.nextStep()?.code).toBe('SCHOOL_SETUP');
  });

  describe('landingFor', () => {
    it('sends a School Admin to the next setup step', () => {
      const svc = setup({ roles: ['School Admin'] });
      let got = '';
      svc.landingFor('/dashboard').subscribe((u) => (got = u));
      http.expectOne(URL).flush(envelope(makeStatus(1)));
      expect(got).toBe('/school-setup/academic-year');
    });

    it('sends a School Admin with a complete setup to the fallback', () => {
      const svc = setup({ roles: ['School Admin'] });
      let got = '';
      svc.landingFor('/dashboard').subscribe((u) => (got = u));
      http.expectOne(URL).flush(envelope(makeStatus(6)));
      expect(got).toBe('/dashboard');
    });

    it('falls back when the status cannot be read', () => {
      const svc = setup({ roles: ['School Admin'] });
      let got = '';
      svc.landingFor('/students').subscribe((u) => (got = u));
      http.expectOne(URL).flush({}, { status: 500, statusText: 'err' });
      expect(got).toBe('/students');
    });

    it('leaves every other role alone, without a request', () => {
      const svc = setup({ roles: ['Principal'] });
      let got = '';
      svc.landingFor('/dashboard').subscribe((u) => (got = u));
      expect(got).toBe('/dashboard');
      http.expectNone(URL);
    });
  });

  describe('a 409 SETUP_INCOMPLETE', () => {
    const blocked = (route = '/school-setup/academic-year') =>
      new HttpErrorResponse({
        status: 409,
        error: { status: false, statusCode: 409, message: 'Finish the school setup first.', data: { code: 'SETUP_INCOMPLETE', nextStep: { code: 'ACADEMIC_YEAR', title: 'Add the academic year', route, done: false, detail: '' } } },
      });

    it('is recognised only by status and code', () => {
      expect(isSetupIncomplete(blocked())).toBe(true);
      expect(isSetupIncomplete(new HttpErrorResponse({ status: 409, error: { data: { code: 'OTHER' } } }))).toBe(false);
      expect(isSetupIncomplete(new HttpErrorResponse({ status: 400, error: { data: { code: 'SETUP_INCOMPLETE' } } }))).toBe(false);
      expect(isSetupIncomplete(new Error('x'))).toBe(false);
    });

    it('offers a School Admin a button to the next step and goes there on yes', async () => {
      const svc = setup({ roles: ['School Admin'] });
      const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      await svc.handleSetupIncomplete(blocked());
      http.expectOne(URL).flush(envelope(makeStatus(1)));
      expect(confirm).toHaveBeenCalledOnce();
      expect(confirm.mock.calls[0][0].message).toContain('Add the academic year');
      expect(confirm.mock.calls[0][0].message).not.toContain('409');
      expect(navigate).toHaveBeenCalledWith('/school-setup/academic-year');
    });

    it('stays where it is when the admin says "Not now"', async () => {
      const svc = setup({ roles: ['School Admin'] });
      vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(false);
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      await svc.handleSetupIncomplete(blocked());
      http.expectOne(URL).flush(envelope(makeStatus(1)));
      expect(navigate).not.toHaveBeenCalled();
    });

    it('never navigates to a route that is not inside this app', async () => {
      const svc = setup({ roles: ['School Admin'] });
      const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
      const toast = vi.spyOn(TestBed.inject(ToastService), 'warning');
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      await svc.handleSetupIncomplete(blocked('https://evil.example/'));
      http.expectOne(URL).flush(envelope(makeStatus(1)));
      expect(confirm).not.toHaveBeenCalled();
      expect(navigate).not.toHaveBeenCalled();
      expect(toast).toHaveBeenCalled();
    });

    it('tells anyone else to ask their School Admin, with no button', async () => {
      const svc = setup({ roles: ['Principal'] });
      const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm');
      const toast = vi.spyOn(TestBed.inject(ToastService), 'warning');
      await svc.handleSetupIncomplete(blocked());
      http.expectOne(URL).flush(envelope(makeStatus(1)));
      expect(confirm).not.toHaveBeenCalled();
      expect(toast.mock.calls[0][0]).toContain('School Admin');
    });
  });
});
