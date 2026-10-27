import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { NoticeListComponent } from './notice-list.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
const notice = (id: number, title: string, audience = 'ALL') => ({
  noticeId: id, title, body: `Body of ${title}`, audience, createdBy: 1, createdByName: 'Admin', publishedAt: '2026-10-27T04:30:00Z', updatedAt: '2026-10-27T04:30:00Z', isRead: false, statusName: 'Active',
});

describe('NoticeListComponent', () => {
  let fixture: ComponentFixture<NoticeListComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(list: unknown) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'School Id', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(NoticeListComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Notice`).flush(env(list));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('lists every notice of the school with its audience in words', () => {
    open([notice(2, 'Staff meeting', 'STAFF'), notice(1, 'Holiday')]);
    const text = el().textContent ?? '';
    expect(text).toContain('Staff meeting');
    expect(text).toContain('Holiday');
    expect(text).toContain('Staff');
    expect(text).toContain('Everyone');
    expect(text).toContain('2 notices');
  });

  it('filtering by audience and date calls the API with those filters', () => {
    open([]);
    cmp()['audienceControl'].setValue('STAFF');
    http.expectOne((r) => r.url === `${API}/api/Notice` && r.params.get('audience') === 'STAFF').flush(env([]));
    cmp()['fromControl'].setValue('2026-10-01');
    http.expectOne((r) => r.url === `${API}/api/Notice` && r.params.get('audience') === 'STAFF' && r.params.get('from') === '2026-10-01').flush(env([]));
  });

  it('New Notice opens the form; an empty submit publishes nothing', () => {
    open([]);
    cmp()['modal'].set({ notice: null });
    fixture.detectChanges();
    expect(el().querySelector('app-notice-form-modal')).not.toBeNull();
    const modal = fixture.debugElement.nativeElement.querySelector('app-notice-form-modal');
    expect(modal.textContent).toContain('Send to');
  });

  it('deleting asks first, then calls DELETE and drops the row', async () => {
    open([notice(1, 'Holiday')]);
    const confirm = TestBed.inject(ConfirmDialogService);
    vi.spyOn(confirm, 'confirm').mockResolvedValue(true);
    await cmp()['remove'](cmp()['rows']()[0]);
    http.expectOne({ method: 'DELETE', url: `${API}/api/Notice/1` }).flush(env({ ...notice(1, 'Holiday'), statusName: 'Inactive' }));
    http.expectOne(`${API}/api/Notice/unread-count`).flush(env({ count: 0 }));
    fixture.detectChanges();
    expect(cmp()['rows']().length).toBe(0);
  });

  it('declining the confirmation deletes nothing', async () => {
    open([notice(1, 'Holiday')]);
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(false);
    await cmp()['remove'](cmp()['rows']()[0]);
    expect(cmp()['rows']().length).toBe(1);
  });
});
