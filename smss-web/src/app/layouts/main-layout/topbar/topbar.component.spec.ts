import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { AuthService } from '../../../core/services/auth.service';
import { clearSessions, makeUser, seedSession } from '../../../core/testing/auth-test-utils';
import { TopbarComponent } from './topbar.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const notice = (id: number, title: string) => ({
  noticeId: id, title, body: 'Body', audience: 'ALL', createdBy: 1, createdByName: 'Admin', publishedAt: '2026-10-27T04:30:00Z', updatedAt: '2026-10-27T04:30:00Z', isRead: false, statusName: 'Active',
});

describe('TopbarComponent notification bell', () => {
  let fixture: ComponentFixture<TopbarComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const bell = () => el().querySelector('button[aria-label="Notifications"]') as HTMLButtonElement;

  function create(schoolId: string | null) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId, userType: 'Employee', roles: ['Teacher'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TopbarComponent);
    fixture.detectChanges();
    if (schoolId) http.expectOne(`${API}/api/SchoolRegistration/me/brand`).flush(env({ schoolName: 'Test School', logoUrl: null }));
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('shows the unread dot only when the user has unread notices', () => {
    create('S');
    http.expectOne(`${API}/api/Notice/unread-count`).flush(env({ count: 3 }));
    fixture.detectChanges();
    expect(el().querySelector('.unread-dot')).not.toBeNull();
  });

  it('no dot when everything is read', () => {
    create('S');
    http.expectOne(`${API}/api/Notice/unread-count`).flush(env({ count: 0 }));
    fixture.detectChanges();
    expect(el().querySelector('.unread-dot')).toBeNull();
  });

  it('opening the bell lists the latest unread notices, each linking to My Notices', () => {
    create('S');
    http.expectOne(`${API}/api/Notice/unread-count`).flush(env({ count: 2 }));
    bell().click();
    http.expectOne((r) => r.url === `${API}/api/Notice/my` && r.params.get('unreadOnly') === 'true').flush(env([notice(9, 'Fee reminder'), notice(8, 'Holiday')]));
    fixture.detectChanges();
    const items = [...el().querySelectorAll('.menu a.menu-item')];
    expect(items.map((a) => a.textContent)).toEqual([expect.stringContaining('Fee reminder'), expect.stringContaining('Holiday'), expect.stringContaining('View all notices')]);
    expect(items[0].getAttribute('href')).toContain('/notices/my?open=9');
  });

  it('the user menu offers "My profile" to an employee login (Teacher) and links it to /my-profile', () => {
    create('S');
    http.expectOne(`${API}/api/Notice/unread-count`).flush(env({ count: 0 }));
    (el().querySelector('button.user') as HTMLButtonElement).click();
    fixture.detectChanges();
    const link = [...el().querySelectorAll('.menu a.menu-item')].find((a) => a.textContent?.includes('My profile'));
    expect(link?.getAttribute('href')).toBe('/my-profile');
  });

  it('the user menu offers a Student "My profile" and links it to /my-student-profile', () => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Student', roles: ['Student'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TopbarComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/SchoolRegistration/me/brand`).flush(env({ schoolName: 'Test School', logoUrl: null }));
    http.expectOne(`${API}/api/Notice/unread-count`).flush(env({ count: 0 }));
    (el().querySelector('button.user') as HTMLButtonElement).click();
    fixture.detectChanges();
    const link = [...el().querySelectorAll('.menu a.menu-item')].find((a) => a.textContent?.includes('My profile'));
    expect(link?.getAttribute('href')).toBe('/my-student-profile');
  });

  it('the user menu does not offer "My profile" to a School Admin (their login is the school, not an employee)', () => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'School Id', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TopbarComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/SchoolRegistration/me/brand`).flush(env({ schoolName: 'Test School', logoUrl: null }));
    http.expectOne(`${API}/api/Notice/unread-count`).flush(env({ count: 0 }));
    (el().querySelector('button.user') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el().textContent).not.toContain('My profile');
    expect(el().textContent).toContain('Change password');
  });

  it('a user without a school (Super Admin) makes no notice requests at all', () => {
    create(null);
    bell().click();
    fixture.detectChanges();
    expect(el().textContent).toContain('No unread notices.');
  });
});

describe('TopbarComponent search', () => {
  let fixture: ComponentFixture<TopbarComponent>;
  let router: Router;
  const el = () => fixture.nativeElement as HTMLElement;
  const type = (value: string) => {
    const input = el().querySelector('.search input') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const results = () => [...el().querySelectorAll('.search-results li')].map((li) => li.textContent?.trim());

  function create(roles: string[], userType: 'Employee' | 'Super Admin' = 'Employee') {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: roles.includes('Super Admin') ? null : 'S', userType, roles }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(TopbarComponent);
    fixture.detectChanges();
    (el().querySelector('button[aria-label="Search"]') as HTMLButtonElement).click();
    fixture.detectChanges();
  }
  afterEach(() => {
    TestBed.inject(HttpTestingController).match(() => true);
    clearSessions();
  });

  it('typing lists the matching pages the role can open (it used to do nothing at all)', () => {
    create(['Super Admin'], 'Super Admin');
    expect(results()).toEqual([]);
    type('gender');
    expect(results()).toEqual(['Gender · Masters']);
    type('zzzz');
    expect(results()).toEqual(['No page matches "zzzz"']);
  });

  it('a role never sees pages it cannot open', () => {
    create(['Teacher']);
    type('masters');
    expect(results()).toEqual(['No page matches "masters"']);
  });

  it('Enter opens the best match and closes the search; Escape closes it and clears the text', () => {
    create(['Super Admin'], 'Super Admin');
    type('blood');
    (el().querySelector('.search input') as HTMLInputElement).dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    fixture.detectChanges();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/masters/blood-group');
    expect(el().querySelector('.search')).toBeNull();

    (el().querySelector('button[aria-label="Search"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    type('gender');
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(el().querySelector('.search')).toBeNull();
    (el().querySelector('button[aria-label="Search"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect((el().querySelector('.search input') as HTMLInputElement).value).toBe('');
  });
});

describe('TopbarComponent school avatar', () => {
  let fixture: ComponentFixture<TopbarComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const avatar = () => el().querySelector('.org .avatar.sm') as HTMLElement;

  function create(roles: string[], sessionLogo: string | null) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles, schoolName: 'Test School', logoUrl: sessionLogo }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TopbarComponent);
    fixture.detectChanges();
  }
  const brand = (logoUrl: string | null, name = 'Test School') => {
    http.expectOne(`${API}/api/SchoolRegistration/me/brand`).flush(env({ schoolName: name, logoUrl }));
    http.match(`${API}/api/Notice/unread-count`).forEach((r) => r.flush(env({ count: 0 })));
    fixture.detectChanges();
  };
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('shows the initial when the school has no logo', () => {
    create(['Teacher'], null);
    brand(null);
    expect(avatar().querySelector('img')).toBeNull();
    expect(avatar().textContent?.trim()).toBe('T');
  });

  it('shows the uploaded logo in the small avatar (any role), from the school brand read, without signing in again', () => {
    create(['Teacher'], null); // the session was made before the logo was uploaded
    brand('/uploads/12345/logo/logo_1.png');
    const img = avatar().querySelector('img')!;
    expect(img.getAttribute('src')).toBe(`${API}/uploads/12345/logo/logo_1.png`);
    expect(avatar().classList).toContain('has-logo');
  });

  it('the logo from the session shows straight away, and a removed logo falls back to the initial', () => {
    create(['Student'], '/uploads/12345/logo/old.png');
    expect(avatar().querySelector('img')).not.toBeNull();
    brand(null);
    expect(avatar().querySelector('img')).toBeNull();
    expect(avatar().textContent?.trim()).toBe('T');
  });

  it('falls back to the initial when the image fails to load', () => {
    create(['Principal'], '/uploads/12345/logo/broken.png');
    brand('/uploads/12345/logo/broken.png');
    avatar().querySelector('img')!.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(avatar().querySelector('img')).toBeNull();
    expect(avatar().textContent?.trim()).toBe('T');
  });

  it('refreshes at once when the School Admin uploads a logo (the profile page patches the session), with no reload', () => {
    create(['School Admin'], null);
    brand(null);
    expect(avatar().querySelector('img')).toBeNull();
    TestBed.inject(AuthService).patchUser({ logoUrl: '/uploads/12345/logo/logo_2.png' });
    fixture.detectChanges();
    expect(avatar().querySelector('img')!.getAttribute('src')).toBe(`${API}/uploads/12345/logo/logo_2.png`);
  });

  it('a failing brand read keeps what the session has', () => {
    create(['Teacher'], '/uploads/12345/logo/keep.png');
    http.expectOne(`${API}/api/SchoolRegistration/me/brand`).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    http.match(`${API}/api/Notice/unread-count`).forEach((r) => r.flush(env({ count: 0 })));
    fixture.detectChanges();
    expect(avatar().querySelector('img')).not.toBeNull();
  });
});
