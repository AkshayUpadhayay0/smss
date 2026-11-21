import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { Observable, isObservable, firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../testing/auth-test-utils';
import { envelope, makeStatus } from '../testing/onboarding-test-utils';
import { ToastService } from '../services/toast.service';
import { setupGuard } from './setup.guard';

const URL = `${environment.apiUrl}/api/Onboarding/status`;

describe('setupGuard', () => {
  let http: HttpTestingController;

  function setup(user: Parameters<typeof makeUser>[0]) {
    clearSessions();
    seedSession({ user: makeUser(user) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
  }

  /** Runs the guard for `url`; resolves to its answer (answering the status request with `status` when one is made). */
  async function run(url: string, status?: 'fail' | ReturnType<typeof makeStatus>) {
    const result = TestBed.runInInjectionContext(() => setupGuard({} as never, { url } as RouterStateSnapshot));
    if (!isObservable(result)) return result as boolean | UrlTree;
    const answer = firstValueFrom(result as Observable<boolean | UrlTree>);
    if (status !== undefined) {
      const req = http.expectOne(URL);
      if (status === 'fail') req.flush({}, { status: 500, statusText: 'err' });
      else req.flush(envelope(status));
    }
    return answer;
  }

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it.each(['/students', '/students/onboard-existing', '/students/import', '/admissions/new', '/employees', '/employees/new', '/transport/areas', '/transport/vehicles', '/transport/drivers'])(
    'redirects an incomplete School Admin from %s to the next setup step, with a message',
    async (url) => {
      setup({ roles: ['School Admin'] });
      const toast = vi.spyOn(TestBed.inject(ToastService), 'warning');
      const result = await run(url, makeStatus(1));
      expect(result).toBeInstanceOf(UrlTree);
      expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/school-setup/academic-year');
      expect(toast).toHaveBeenCalledOnce();
      expect(toast.mock.calls[0][0]).toContain('Add the academic year');
    },
  );

  it('lets a School Admin with a complete setup through', async () => {
    setup({ roles: ['School Admin'] });
    expect(await run('/students', makeStatus(6))).toBe(true);
  });

  it('lets a grandfathered school through even though its steps are not done', async () => {
    setup({ roles: ['School Admin'] });
    expect(await run('/students', makeStatus(1, { isComplete: true, nextStep: null }))).toBe(true);
  });

  it.each(['/dashboard', '/school-setup/classes', '/school-setup/import-masters', '/fee-setup/fee-types', '/fee-setup/fee-structures/new', '/change-password', '/students-report'])(
    'never touches %s (setup and fee screens stay open), and asks nothing',
    async (url) => {
      setup({ roles: ['School Admin'] });
      expect(await run(url)).toBe(true);
      http.expectNone(URL);
    },
  );

  it('opens the page when the status cannot be read (the API still refuses the save)', async () => {
    setup({ roles: ['School Admin'] });
    expect(await run('/employees', 'fail')).toBe(true);
  });

  it('only applies to a School Admin: other roles pass without a request', async () => {
    setup({ roles: ['Principal'] });
    expect(await run('/transport/areas')).toBe(true);
    http.expectNone(URL);
  });

  it('leaves a first-login user to the password guard', async () => {
    setup({ roles: ['School Admin'], isFirstLogin: true });
    expect(await run('/students')).toBe(true);
    http.expectNone(URL);
  });

  it('asks again each time while incomplete, so a step finished elsewhere is noticed', async () => {
    setup({ roles: ['School Admin'] });
    expect(await run('/students', makeStatus(5))).toBeInstanceOf(UrlTree);
    expect(await run('/students', makeStatus(6))).toBe(true);
  });
});
