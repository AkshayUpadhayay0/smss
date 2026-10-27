import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
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

  it('a user without a school (Super Admin) makes no notice requests at all', () => {
    create(null);
    bell().click();
    fixture.detectChanges();
    expect(el().textContent).toContain('No unread notices.');
  });
});
