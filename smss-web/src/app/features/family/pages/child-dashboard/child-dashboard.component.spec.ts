import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { ChildDashboardComponent } from './child-dashboard.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const card = (id: string, name: string, over: Record<string, unknown> = {}) => ({ studentId: id, fullName: name, admissionNumber: 'A1', statusName: 'Active', profilePhotoUrl: null, className: 'Class 3', sectionName: 'A', chipState: null, readOnly: false, attendanceToday: null, feeDueNow: 0, ...over });
const CHILDREN = { children: [card('S1', 'Aarav Rao'), card('S2', 'Meera Rao', { chipState: 'PASSED_OUT', readOnly: true })], feeTile: { allPaid: true, totalDueNow: 0, children: [] } };

describe('ChildDashboardComponent', () => {
  let fixture: ComponentFixture<ChildDashboardComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');

  function open(studentId: string, tab?: string, list: unknown = CHILDREN) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['Guardian'], userType: 'Guardian' }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ChildDashboardComponent);
    fixture.componentRef.setInput('studentId', studentId);
    if (tab) fixture.componentRef.setInput('tab', tab);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Family/children`).flush(env(list));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('opens the child from the family list and loads the profile tab only (nothing else is asked for)', () => {
    open('S1');
    expect(el().querySelector('[data-field="name"]')?.textContent).toBe('Aarav Rao');
    const req = http.expectOne(`${API}/api/Family/children/S1/profile`);
    expect(req.request.method).toBe('GET');
    req.flush(env({ studentId: 'S1', fullName: 'Aarav Rao', admissionNumber: 'A1', statusName: 'Active', dateOfBirth: '2015-04-05T00:00:00', className: 'Class 3', sectionName: 'A', genderName: 'Male' }));
    fixture.detectChanges();
    expect(text()).toContain('Admission number');
    expect(el().querySelector('[data-banner="read-only"]')).toBeNull();
    http.expectNone((r) => r.url.includes('attendance') || r.url.includes('fees'));
  });

  it('a read-only child shows a clear banner; the header switcher lists the children and moves to the one chosen, keeping the tab', () => {
    open('S2', 'leave');
    http.expectOne(`${API}/api/Family/children/S2/leave`).flush(env([]));
    fixture.detectChanges();
    expect(el().querySelector('[data-banner="read-only"]')?.textContent).toContain('read-only record');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const select = el().querySelector('[data-field="switcher"]') as HTMLSelectElement;
    expect([...select.options].map((o) => o.value)).toEqual(['S1', 'S2']);
    select.value = 'S1'; select.dispatchEvent(new Event('change'));
    expect(navigate).toHaveBeenCalledWith(['/family/children', 'S1'], { queryParams: { tab: 'leave' } });
    expect(el().querySelector('[data-state="empty"]')?.textContent).toContain('No leave');
  });

  it('a child that is not on this parent\'s list is "not found": nothing about it is requested', () => {
    open('NOT-MINE');
    expect(el().querySelector('[data-state="not-found"]')).not.toBeNull();
    http.expectNone((r) => r.url.includes('NOT-MINE'));
  });

  it('a pending-promotion child says it is last year\'s record', () => {
    open('S1', 'profile', { ...CHILDREN, children: [card('S1', 'Aarav Rao', { chipState: 'PENDING_PROMOTION', readOnly: true })] });
    http.expectOne(`${API}/api/Family/children/S1/profile`).flush(env({ studentId: 'S1', fullName: 'Aarav Rao', admissionNumber: 'A1', statusName: 'Active', dateOfBirth: '2015-04-05T00:00:00' }));
    fixture.detectChanges();
    expect(el().querySelector('[data-banner="read-only"]')?.textContent).toContain('last year');
  });

  it('every tab asks only the Family endpoint of its own kind (no attachment, no write)', () => {
    const urls: Record<string, string> = { attendance: 'attendance', homework: 'homework', timetable: 'timetable', exams: 'exam-schedule', reports: 'report-cards', fees: 'fees', leave: 'leave' };
    for (const [tab, path] of Object.entries(urls)) {
      TestBed.resetTestingModule();
      open('S1', tab);
      const req = http.expectOne((r) => r.url.startsWith(`${API}/api/Family/children/S1/${path}`));
      expect(req.request.method).toBe('GET');
      req.flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();
      expect([tab, el().querySelector('[data-state="failed"]') !== null]).toEqual([tab, true]);   // a failure is calm, with a retry
    }
  });

  it('the fees tab shows the late fee line of a payment and opens a receipt through the Family endpoint', () => {
    open('S1', 'fees');
    http.expectOne(`${API}/api/Family/children/S1/fees`).flush(env({
      hasFees: true, overallStatus: 'Partially Paid', totalDue: 1000, totalPaid: 400, balance: 600, overdueAmount: 0, overdueCount: 0, heads: [],
      dues: [{ feeTypeName: 'Tuition', dueDate: '2026-09-01', amountDue: 1000, amountPaid: 400, balance: 600, status: 'Partially Paid' }],
      payments: [{ paymentId: 9, paymentDate: '2026-10-01T00:00:00Z', amount: 450, receiptNumber: 'RCP-2026-0009', paymentMode: 'Cash', lateFee: { decision: 'APPLIED', amount: 50, taxAmount: 0, ruleText: '₹50 flat', lines: [] } }],
    }));
    fixture.detectChanges();
    expect(el().querySelector('[data-field="late-fee"]')?.textContent).toContain('late fee');
    (el().querySelector('[data-action="receipt"]') as HTMLButtonElement).click();
    const req = http.expectOne(`${API}/api/Family/children/S1/receipts/9`);
    expect(req.request.method).toBe('GET');
    req.flush({ status: false, statusCode: 404, message: 'x', data: null }, { status: 404, statusText: 'Not Found' });
  });

  it('a report card downloads through the Family pdf endpoint', () => {
    open('S1', 'reports');
    http.expectOne(`${API}/api/Family/children/S1/report-cards`).flush(env([{ publicationId: 5, schemeName: 'Term cards', className: 'Class 3', sectionName: 'A', scope: 'TERM', termName: 'Term 1', version: 1, publishedAt: '2026-10-01T00:00:00Z', result: 'PASS', percent: 82, grade: 'A', rank: null }]));
    fixture.detectChanges();
    expect(text()).toContain('Term cards');
    (el().querySelector('[data-action="pdf"]') as HTMLButtonElement).click();
    const req = http.expectOne(`${API}/api/Family/children/S1/report-cards/5/pdf`);
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['%PDF'], { type: 'application/pdf' }), { status: 404, statusText: 'x' });
  });
});
