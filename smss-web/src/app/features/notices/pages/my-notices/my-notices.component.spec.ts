import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { MyNoticesComponent } from './my-notices.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
const notice = (id: number, title: string, isRead: boolean, extra = {}) => ({
  noticeId: id, title, body: `Body of ${title}`, audience: 'ALL', createdBy: 1, createdByName: 'Admin', publishedAt: '2026-10-27T04:30:00Z', updatedAt: '2026-10-27T04:30:00Z', isRead, statusName: 'Active', ...extra,
});

describe('MyNoticesComponent', () => {
  let fixture: ComponentFixture<MyNoticesComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;

  function open(list: unknown, unread = 2) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Student', roles: ['Student'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyNoticesComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Notice/my`).flush(env(list));
    http.expectOne(`${API}/api/Notice/unread-count`).flush(env({ count: unread }));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('lists the notices as the API ordered them, marking the unread ones, with the unread total', () => {
    open([notice(3, 'Newest', false), notice(2, 'Middle', true), notice(1, 'Oldest', false)]);
    expect([...el().querySelectorAll('.card .title')].map((t) => t.firstChild?.textContent?.trim())).toEqual(['Newest', 'Middle', 'Oldest']);
    expect([...el().querySelectorAll('.card')].map((c) => c.classList.contains('unread'))).toEqual([true, false, true]);
    expect(el().textContent).toContain('2 unread');
  });

  it('opening an unread notice asks the server for it (which records the read), shows it in full and refreshes the count', () => {
    open([notice(3, 'Newest', false)], 1);
    (el().querySelector('.card .head') as HTMLButtonElement).click();
    http.expectOne(`${API}/api/Notice/3`).flush(env(notice(3, 'Newest', true)));
    http.expectOne(`${API}/api/Notice/unread-count`).flush(env({ count: 0 }));
    fixture.detectChanges();
    expect(el().querySelector('.card .body')?.textContent).toContain('Body of Newest');
    expect(el().querySelector('.card')?.classList.contains('unread')).toBe(false);
    expect(el().textContent).toContain('0 unread');
  });

  it('opening a notice that is already read does not call the API again', () => {
    open([notice(2, 'Seen', true)], 0);
    (el().querySelector('.card .head') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el().querySelector('.card .body')).not.toBeNull();
  });

  it('the unread-only box reloads with unreadOnly=true', () => {
    open([notice(1, 'A', false)]);
    const box = el().querySelector('input[type=checkbox]') as HTMLInputElement;
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    const req = http.expectOne((r) => r.url === `${API}/api/Notice/my` && r.params.get('unreadOnly') === 'true');
    req.flush(env([]));
    http.expectOne(`${API}/api/Notice/unread-count`).flush(env({ count: 0 }));
    fixture.detectChanges();
    expect(el().textContent).toContain('You have no unread notices.');
  });

  it('shows an empty state when nothing is addressed to the user', () => {
    open([], 0);
    expect(el().textContent).toContain('There are no notices for you.');
  });
});
