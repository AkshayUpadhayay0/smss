import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { AuthService } from '../../../core/services/auth.service';
import { clearSessions, makeUser, seedSession } from '../../../core/testing/auth-test-utils';
import { ParentLayoutComponent } from '../layout/parent-layout.component';
import { FamilyService } from '../services/family.service';
import { FamilyAccountComponent, FamilyCalendarComponent, FamilyNoticesComponent } from './family-pages';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

function setup() {
  clearSessions();
  seedSession({ user: makeUser({ schoolId: 'S', roles: ['Guardian'], userType: 'Guardian', displayName: 'Asha Rao', orgUserId: 'GHS001GD001' }) });
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
  return TestBed.inject(HttpTestingController);
}

describe('Family notices', () => {
  let fixture: ComponentFixture<FamilyNoticesComponent>; let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  afterEach(() => { http.verify(); clearSessions(); });

  it('lists the notices, marks one read only when it is opened, and refreshes the unread badge', () => {
    http = setup();
    fixture = TestBed.createComponent(FamilyNoticesComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Family/notices`).flush(env([{ noticeId: 4, title: 'PTM on Friday', body: 'Please come at 10.', publishedAt: '2026-11-20T00:00:00Z', isRead: false, postedBy: 'Office' }, { noticeId: 5, title: 'Old', body: 'b', publishedAt: '2026-10-01T00:00:00Z', isRead: true }]));
    fixture.detectChanges();
    expect(el().querySelectorAll('[data-notice]').length).toBe(2);
    expect(el().querySelector('[data-field="body"]')).toBeNull();
    (el().querySelector('[data-notice="4"] button') as HTMLButtonElement).click();
    http.expectOne(`${API}/api/Family/notices/4`).flush(env({ noticeId: 4, title: 'PTM on Friday', body: 'Please come at 10.', publishedAt: '2026-11-20T00:00:00Z', isRead: true }));
    http.expectOne(`${API}/api/Family/notices/unread-count`).flush(env({ count: 0 }));
    fixture.detectChanges();
    expect(el().querySelector('[data-field="body"]')?.textContent).toContain('Please come at 10.');
    expect(el().querySelector('[data-notice="4"]')?.classList.contains('unread')).toBe(false);
    expect(TestBed.inject(FamilyService).unread()).toBe(0);
  });

  it('empty and failed states are calm', () => {
    http = setup();
    fixture = TestBed.createComponent(FamilyNoticesComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Family/notices`).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(el().querySelector('[data-state="failed"]')).not.toBeNull();
    (el().querySelector('[data-action="retry"]') as HTMLButtonElement).click();
    http.expectOne(`${API}/api/Family/notices`).flush(env([]));
    fixture.detectChanges();
    expect(el().querySelector('[data-state="empty"]')).not.toBeNull();
  });
});

describe('Family calendar', () => {
  let fixture: ComponentFixture<FamilyCalendarComponent>; let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  afterEach(() => { http.verify(); clearSessions(); });

  it('shows the month from the Family calendar endpoint, and moves to the next month', () => {
    http = setup();
    fixture = TestBed.createComponent(FamilyCalendarComponent);
    fixture.detectChanges();
    const first = http.expectOne((r) => r.url === `${API}/api/Family/calendar`);
    expect(first.request.params.get('from')).toMatch(/^\d{4}-\d{2}-01$/);
    first.flush(env([{ eventId: 1, title: 'Sports Day', startDate: '2026-11-28', endDate: '2026-11-28', isAllDay: true, categoryName: 'School Event', colorHex: '#8b5cf6' }]));
    fixture.detectChanges();
    expect(el().querySelector('[data-event="1"]')?.textContent).toContain('Sports Day');
    (el().querySelector('[data-action="next"]') as HTMLButtonElement).click();
    http.expectOne((r) => r.url === `${API}/api/Family/calendar`).flush(env([]));
    fixture.detectChanges();
    expect(el().querySelector('[data-state="empty"]')?.textContent).toContain('Nothing is planned');
  });
});

describe('Family account and the parent shell', () => {
  afterEach(() => clearSessions());

  it('the account page shows the login id and signs out to the login page', () => {
    const http = setup();
    const fixture = TestBed.createComponent(FamilyAccountComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-field="login-id"]')?.textContent).toBe('GHS001GD001');
    expect((el.querySelector('[data-link="change-password"]') as HTMLAnchorElement).getAttribute('href')).toBe('/family/account/change-password');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    vi.spyOn(TestBed.inject(AuthService), 'logout').mockReturnValue({ subscribe: (f: () => void) => f() } as never);
    (el.querySelector('[data-action="sign-out"]') as HTMLButtonElement).click();
    expect(navigate).toHaveBeenCalledWith(['/login']);
    http.verify();
  });

  it('the shell has exactly four links, no staff sidebar, the school and parent names, and the unread badge', () => {
    const http = setup();
    const fixture = TestBed.createComponent(ParentLayoutComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Family/notices/unread-count`).flush(env({ count: 2 }));
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect([...el.querySelectorAll('nav a')].map((a) => a.getAttribute('data-nav'))).toEqual(['Family', 'Notices', 'Calendar', 'Account']);
    expect([...el.querySelectorAll('nav a')].map((a) => a.getAttribute('href'))).toEqual(['/family', '/family/notices', '/family/calendar', '/family/account']);
    expect(el.querySelector('app-sidebar, app-topbar, aside')).toBeNull();
    expect(el.querySelector('[data-field="unread"]')?.textContent).toBe('2');
    expect(el.querySelector('[data-field="parent"]')?.textContent).toBe('Asha Rao');
    http.verify();
  });

  it('the shell shows no badge when the count cannot be read or is zero', () => {
    const http = setup();
    const fixture = TestBed.createComponent(ParentLayoutComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Family/notices/unread-count`).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-field="unread"]')).toBeNull();
    http.verify();
  });
});
