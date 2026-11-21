import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../core/testing/auth-test-utils';
import { envelope, makeStatus } from '../../../core/testing/onboarding-test-utils';
import { OnboardingService } from '../../../core/services/onboarding.service';
import { SidebarComponent } from './sidebar.component';

const STATUS = `${environment.apiUrl}/api/Onboarding/status`;

describe('SidebarComponent: menu while the school setup is unfinished', () => {
  let http: HttpTestingController;

  function open(roles: string[], status: ReturnType<typeof makeStatus> | null) {
    clearSessions();
    seedSession({ user: makeUser({ roles, schoolId: 'S', userType: 'School Id' }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(SidebarComponent);
    const onboarding = TestBed.inject(OnboardingService);
    if (status) {
      onboarding.refresh().subscribe();
      http.expectOne(STATUS).flush(envelope(status));
    }
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, onboarding };
  }

  /** The sidebar entry (leaf link or group toggle) with this label. */
  const entry = (el: HTMLElement, label: string) =>
    Array.from(el.querySelectorAll<HTMLElement>('.link')).find((e) => e.querySelector('.label')?.textContent?.trim() === label) ?? null;

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('disables Employees and Transport with a tooltip that says why, while the setup is incomplete', () => {
    const { el } = open(['School Admin'], makeStatus(2));
    for (const label of ['Employees', 'Transport']) {
      const e = entry(el, label)!;
      expect(e, label).not.toBeNull();
      expect(e.getAttribute('aria-disabled'), label).toBe('true');
      expect((e as HTMLButtonElement).disabled, label).toBe(true);
      expect(e.getAttribute('title'), label).toContain('Finish the school setup first');
      expect(e.getAttribute('title'), label).toContain('Import masters or add classes');
    }
  });

  it('keeps the setup and fee screens clickable', () => {
    const { el } = open(['School Admin'], makeStatus(2));
    for (const label of ['School Setup', 'Fee Setup']) expect(entry(el, label)!.getAttribute('aria-disabled'), label).toBeNull();
  });

  it('locks the student entries inside the Students group (opened by its route) but not Student Leave', () => {
    const { fixture, el } = open(['School Admin'], makeStatus(2));
    const group = entry(el, 'Students')!;
    expect(group.getAttribute('aria-disabled')).toBeNull();
    group.click();
    fixture.detectChanges();
    const locked = Array.from(el.querySelectorAll('.child.locked')).map((e) => e.querySelector('.label')?.textContent?.trim());
    expect(locked).toEqual(expect.arrayContaining(['Students', 'Admissions', 'Add Existing Student', 'Import Students']));
    expect(locked).not.toContain('Student Leave');
    expect(locked).not.toContain('Fee Status');
    expect(el.querySelector('a.child[href="/students"]')).toBeNull();
  });

  it('enables everything again as soon as the status becomes complete', () => {
    const { fixture, el, onboarding } = open(['School Admin'], makeStatus(5));
    expect(entry(el, 'Employees')!.getAttribute('aria-disabled')).toBe('true');
    onboarding.refresh().subscribe();
    http.expectOne(STATUS).flush(envelope(makeStatus(6)));
    fixture.detectChanges();
    expect(entry(el, 'Employees')!.getAttribute('aria-disabled')).toBeNull();
    expect(el.querySelector('.locked')).toBeNull();
  });

  it('does not lock anything while the status is unknown', () => {
    const { el } = open(['School Admin'], null);
    expect(el.querySelector('.locked')).toBeNull();
  });

  it('shows a Principal the same lock with a tooltip for their School Admin', () => {
    const { el } = open(['Principal'], makeStatus(2));
    const transport = entry(el, 'Transport');
    if (transport) expect(transport.getAttribute('title')).toContain('Your School Admin must finish it first');
    expect(el.querySelector('.locked')).not.toBeNull();
  });
});
