import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { envelope, makeStatus } from '../../../../core/testing/onboarding-test-utils';
import { OnboardingService } from '../../../../core/services/onboarding.service';
import { SetupProgressComponent } from './setup-progress.component';

const STATUS = `${environment.apiUrl}/api/Onboarding/status`;

describe('SetupProgressComponent', () => {
  let http: HttpTestingController;

  async function open(roles: string[], url: string, status: ReturnType<typeof makeStatus> | null) {
    clearSessions();
    seedSession({ user: makeUser({ roles, schoolId: 'S' }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: '**', children: [] }])] });
    http = TestBed.inject(HttpTestingController);
    await TestBed.inject(Router).navigateByUrl(url);
    if (status) {
      TestBed.inject(OnboardingService).refresh().subscribe();
      http.expectOne(STATUS).flush(envelope(status));
    }
    const fixture = TestBed.createComponent(SetupProgressComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('shows a School Admin the checklist on the dashboard, every step linking to its screen', async () => {
    const el = await open(['School Admin'], '/dashboard', makeStatus(2));
    expect(el.querySelector('.setup')).not.toBeNull();
    expect(el.textContent).toContain('2 of 6 steps done');
    const links = Array.from(el.querySelectorAll<HTMLAnchorElement>('a.step-link')).map((a) => a.getAttribute('href'));
    expect(links).toEqual(['/change-password', '/school-setup/academic-year', '/school-setup/import-masters', '/school-setup/sections', '/fee-setup/fee-types', '/fee-setup/fee-structures']);
    expect(el.querySelectorAll('li.done').length).toBe(2);
    expect(el.querySelector('li.next .step-title')?.textContent).toContain('Import masters or add classes');
    expect(el.querySelector('.detail')?.textContent).toContain('Still to do');
  });

  it('shows the compact strip above the setup screens and the fee screens', async () => {
    for (const url of ['/school-setup/classes', '/fee-setup/fee-types']) {
      TestBed.resetTestingModule();
      const el = await open(['School Admin'], url, makeStatus(3));
      expect(el.querySelector('.setup.compact'), url).not.toBeNull();
    }
  });

  it('shows nothing on other pages', async () => {
    const el = await open(['School Admin'], '/calendar', makeStatus(2));
    expect(el.querySelector('.setup, .banner')).toBeNull();
  });

  it('shows nothing once the setup is complete, or while the status is unknown', async () => {
    expect((await open(['School Admin'], '/dashboard', makeStatus(6))).querySelector('.setup, .banner')).toBeNull();
    TestBed.resetTestingModule();
    expect((await open(['School Admin'], '/dashboard', null)).querySelector('.setup, .banner')).toBeNull();
  });

  it.each([['Principal'], ['Accountant']])('shows a %s only a "School setup is incomplete" banner, no checklist', async (role) => {
    const el = await open([role], '/dashboard', makeStatus(2));
    expect(el.querySelector('.banner')?.textContent).toContain('School setup is incomplete');
    expect(el.querySelector('.setup')).toBeNull();
    expect(el.querySelector('a')).toBeNull();
  });
});
