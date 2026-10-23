import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { PeriodFormModalComponent } from '../../components/period-form-modal/period-form-modal.component';
import { PeriodListComponent } from './period-list.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
const per = (periodId: number, periodNumber: number, start: string, end: string, activeEntryCount = 0, statusName = 'Active') => ({ periodId, periodNumber, startTime: `${start}:00`, endTime: `${end}:00`, statusName, activeEntryCount });

describe('PeriodListComponent', () => {
  let fixture: ComponentFixture<PeriodListComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(list = [per(2, 2, '08:45', '09:30', 3), per(1, 1, '08:00', '08:45', 5)]) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PeriodListComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Period`).flush(env(list));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('lists the periods in number order with trimmed times and how many slots use each', () => {
    open();
    const rows = cmp()['rows']();
    expect(rows.map((r: { periodNumber: number }) => r.periodNumber)).toEqual([1, 2]);
    expect(rows[0].time).toBe('08:00 – 08:45');
    expect(rows[0].usage).toBe('5 slots');
    expect(cmp()['nextNumber']()).toBe(3);
  });

  it('Add Period suggests the next number; Save posts HH:mm times and adds the new period to the list', () => {
    open();
    cmp()['modal'].set({ period: null });
    fixture.detectChanges();
    const modal = fixture.debugElement.query((d) => d.name === 'app-period-form-modal').componentInstance as PeriodFormModalComponent & Record<string, any>;
    expect(modal['form'].controls.periodNumber.value).toBe('3');
    modal['form'].patchValue({ startTime: '09:40', endTime: '10:25' });
    modal['submit']();
    const post = http.expectOne(`${API}/api/Period`);
    expect(post.request.body).toEqual({ periodNumber: 3, startTime: '09:40', endTime: '10:25' });
    post.flush(env(per(3, 3, '09:40', '10:25'), 201));
    fixture.detectChanges();
    expect(cmp()['modal']()).toBeNull();
    expect(cmp()['periods']()).toHaveLength(3);
  });

  it('refuses an end time that is not after the start, and a number outside 1-30, without calling the API', () => {
    open();
    cmp()['modal'].set({ period: null });
    fixture.detectChanges();
    const modal = fixture.debugElement.query((d) => d.name === 'app-period-form-modal').componentInstance as Record<string, any>;
    modal['form'].patchValue({ periodNumber: '3', startTime: '10:00', endTime: '09:00' });
    modal['submit']();
    expect(modal['error']('endTime')).toBe('End time must be after the start time');
    modal['form'].patchValue({ periodNumber: '31', startTime: '09:00', endTime: '10:00' });
    modal['submit']();
    expect(modal['error']('periodNumber')).toContain('1 to 30');
    http.expectNone(`${API}/api/Period`);
  });

  it('Edit starts on the saved values and PUTs to the period', () => {
    open();
    cmp()['openEdit'](cmp()['rows']()[0]);
    fixture.detectChanges();
    const modal = fixture.debugElement.query((d) => d.name === 'app-period-form-modal').componentInstance as Record<string, any>;
    expect(modal['form'].getRawValue()).toEqual({ periodNumber: '1', startTime: '08:00', endTime: '08:45' });
    modal['form'].patchValue({ endTime: '08:50' });
    modal['submit']();
    const put = http.expectOne(`${API}/api/Period/1`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ periodNumber: 1, startTime: '08:00', endTime: '08:50' });
    put.flush(env(per(1, 1, '08:00', '08:50', 5)));
    expect(cmp()['periods']().find((p: { periodId: number }) => p.periodId === 1).endTime).toBe('08:50:00');
  });
});
