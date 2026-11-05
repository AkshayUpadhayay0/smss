import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { StudentMyFees } from '../../models/student.model';
import { StudentMyFeesComponent } from './student-my-fees.component';

const URL = `${environment.apiUrl}/api/Student/me/fees`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const FEES: StudentMyFees = {
  academicYearName: '2026-27', hasFees: true, overallStatus: 'Overdue', totalDue: 48700, totalPaid: 11600, balance: 37100, overdueAmount: 20000, overdueCount: 2,
  heads: [
    { feeTypeName: 'Tuition Fee', frequencyName: 'Monthly', amount: 2500, concession: 250, netAmount: 2250, annualNetAmount: 27000 },
    { feeTypeName: 'Annual Charges', frequencyName: 'Annual', amount: 5000, concession: 0, netAmount: 5000, annualNetAmount: 5000 },
  ],
  dues: [
    { feeTypeName: 'Tuition Fee', dueDate: '2026-04-10', amountDue: 2250, amountPaid: 2250, balance: 0, status: 'Paid' },
    { feeTypeName: 'Tuition Fee', dueDate: '2026-05-10', amountDue: 2250, amountPaid: 1000, balance: 1250, status: 'Overdue' },
    { feeTypeName: 'Tuition Fee', dueDate: '2027-01-10', amountDue: 2250, amountPaid: 0, balance: 2250, status: 'Pending' },
  ],
  payments: [{ paymentDate: '2026-04-09T10:00:00Z', amount: 3250, receiptNumber: 'RCP-2026-0001', paymentMode: 'UPI' }],
};

describe('StudentMyFeesComponent', () => {
  let fixture: ComponentFixture<StudentMyFeesComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');

  function open(respond: (r: ReturnType<HttpTestingController['expectOne']>) => void) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Student', roles: ['Student'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StudentMyFeesComponent);
    fixture.detectChanges();
    respond(http.expectOne(URL));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('shows total, paid, balance and the overall status', () => {
    open((r) => r.flush(env(FEES)));
    const tiles = [...el().querySelectorAll('.tile')].map((t) => (t.textContent ?? '').replace(/\s+/g, ' ').trim());
    expect(tiles[0]).toContain('48,700');
    expect(tiles[1]).toContain('11,600');
    expect(tiles[2]).toContain('37,100');
    expect(tiles[3]).toContain('Overdue');
    expect(tiles[3]).toContain('2 overdue');
  });

  it('lists every instalment with its due date, amounts and status pill', () => {
    open((r) => r.flush(env(FEES)));
    const rows = el().querySelectorAll('.dues .row:not(.head)');
    expect(rows.length).toBe(3);
    expect(rows[1].textContent).toContain('10 May 2026');
    expect(rows[1].textContent).toContain('1,250');
    expect([...rows].map((r) => r.querySelector('.pill')?.textContent?.trim())).toEqual(['Paid', 'Overdue', 'Pending']);
  });

  it('shows fee heads (with the concession already included) and the payment history with receipt numbers', () => {
    open((r) => r.flush(env(FEES)));
    expect(text()).toContain('includes a concession of');
    expect(text()).toContain('Receipt RCP-2026-0001 · UPI');
  });

  it('is read-only: no pay button, no inputs, no edit controls', () => {
    open((r) => r.flush(env(FEES)));
    expect(el().querySelectorAll('input, select, textarea').length).toBe(0);
    expect(el().textContent).not.toMatch(/pay now|collect|void|edit/i);
    expect([...el().querySelectorAll('button')].length).toBe(0);
  });

  it('shows a calm empty state when no fees are assigned', () => {
    open((r) => r.flush(env({ ...FEES, hasFees: false, overallStatus: 'Not Set', dues: [], heads: [], payments: [], totalDue: 0, totalPaid: 0, balance: 0, overdueCount: 0, overdueAmount: 0 })));
    expect(text()).toContain('No fees have been assigned to you yet for 2026-27');
    expect(el().querySelector('.dues')).toBeNull();
  });

  it('says so when nothing has been paid yet', () => {
    open((r) => r.flush(env({ ...FEES, payments: [] })));
    expect(text()).toContain('No payment has been recorded yet.');
  });

  it('a failed load shows one calm message with a retry', () => {
    open((r) => r.flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' }));
    expect(el().querySelector('[role="alert"]')?.textContent).toContain('could not be loaded');
    (el().querySelector('button') as HTMLButtonElement).click();
    http.expectOne(URL).flush(env(FEES));
    fixture.detectChanges();
    expect(el().querySelector('.dues')).not.toBeNull();
  });
});
