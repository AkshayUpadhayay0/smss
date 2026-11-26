import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { FamilyChildren } from '../../models/family.model';
import { FamilyService } from '../../services/family.service';
import { FamilyHomeComponent } from './family-home.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const card = (id: string, name: string, over: Record<string, unknown> = {}) => ({ studentId: id, fullName: name, admissionNumber: 'A1', statusName: 'Active', profilePhotoUrl: null, className: 'Class 3', sectionName: 'A', chipState: null, readOnly: false, attendanceToday: 'Present', feeDueNow: 0, ...over });
const data = (over: Partial<FamilyChildren> = {}): FamilyChildren => ({ children: [card('GHS001ST001', 'Aarav Rao') as never], feeTile: { allPaid: true, totalDueNow: 0, children: [] }, ...over });

describe('FamilyHomeComponent', () => {
  let fixture: ComponentFixture<FamilyHomeComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');

  function open(first: FamilyChildren | 'fail' | null = data()) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['Guardian'], userType: 'Guardian' }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(FamilyHomeComponent);
    fixture.detectChanges();
    if (first !== null) {
      const req = http.expectOne(`${API}/api/Family/children`);
      expect(req.request.method).toBe('GET');
      if (first === 'fail') req.flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' }); else req.flush(env(first));
      fixture.detectChanges();
    }
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('shows a loading state, then one card per child even with a single child, with the class and the student id', () => {
    open(null);
    expect(el().querySelector('[data-state="loading"]')).not.toBeNull();
    http.expectOne(`${API}/api/Family/children`).flush(env(data()));
    fixture.detectChanges();
    expect(el().querySelectorAll('[data-child]').length).toBe(1);
    expect(text()).toContain('Aarav Rao'); expect(text()).toContain('GHS001ST001'); expect(text()).toContain('Class 3 – A');
    expect(el().querySelector('[data-chip="attendance"]')?.textContent).toContain('Today: Present');
  });

  it('the fee tile says Paid only when every child is paid; otherwise the total due now and only the children who owe', () => {
    open(data());
    expect(el().querySelector('[data-field="fee-paid"]')?.textContent).toContain('Paid');
    TestBed.resetTestingModule();
    open(data({ children: [card('S1', 'Aarav', { feeDueNow: 500 }) as never, card('S2', 'Meera') as never], feeTile: { allPaid: false, totalDueNow: 500, children: [{ studentId: 'S1', fullName: 'Aarav', dueNow: 500 }] } }));
    expect(el().querySelector('[data-field="fee-due"]')?.textContent).toContain('₹500');
    expect(el().querySelectorAll('[data-fee-child]').length).toBe(1);
    expect(el().querySelector('[data-fee-child="S1"]')).not.toBeNull();
    expect(el().querySelector('[data-chip="fee"]')).not.toBeNull();
  });

  it('a passed-out, left, transferred or not-yet-promoted child shows its read-only chip instead of today\'s attendance', () => {
    open(data({ children: ['PASSED_OUT', 'LEFT', 'TRANSFERRED_OUT', 'PENDING_PROMOTION'].map((chip, i) => card(`S${i}`, `Child ${i}`, { chipState: chip, readOnly: true, attendanceToday: null }) as never) }));
    const chips = [...el().querySelectorAll('[data-child]')].map((c) => c.querySelector('[data-chip]')?.getAttribute('data-chip'));
    expect(chips).toEqual(['PASSED_OUT', 'LEFT', 'TRANSFERRED_OUT', 'PENDING_PROMOTION']);
    expect(text()).toContain('Passed out'); expect(text()).toContain('Waiting to be promoted');
    expect(el().querySelector('[data-chip="attendance"]')).toBeNull();
  });

  it('has the School Notifications button with the unread count and the Academic Calendar button, both inside the parent shell', () => {
    open(data());
    TestBed.inject(FamilyService).unread.set(3);
    fixture.detectChanges();
    const notices = el().querySelector('[data-action="notices"]') as HTMLAnchorElement;
    expect(notices.getAttribute('href')).toBe('/family/notices');
    expect(el().querySelector('[data-field="unread"]')?.textContent).toBe('3');
    expect((el().querySelector('[data-action="calendar"]') as HTMLAnchorElement).getAttribute('href')).toBe('/family/calendar');
  });

  it('a failure says so calmly and offers a retry; no children is a calm empty state, not an error', () => {
    open('fail');
    expect(el().querySelector('[data-state="failed"]')).not.toBeNull();
    (el().querySelector('[data-action="retry"]') as HTMLButtonElement).click();
    http.expectOne(`${API}/api/Family/children`).flush(env(data({ children: [], feeTile: { allPaid: true, totalDueNow: 0, children: [] } })));
    fixture.detectChanges();
    expect(el().querySelector('[data-state="empty"]')?.textContent).toContain('No children are linked');
  });

  it('never asks for anything but the Family children list, and no id or token is put in a URL', () => {
    open(data());
    expect(el().innerHTML).not.toMatch(/token|password/i);
  });
});
