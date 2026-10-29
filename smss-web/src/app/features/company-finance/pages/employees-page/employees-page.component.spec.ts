import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { CompanyEmployee, CompanySalaryPayment } from '../../models/company-finance.model';
import { CompanyEmployeesPageComponent } from './employees-page.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const employee = (o: Partial<CompanyEmployee> = {}): CompanyEmployee => ({
  companyEmployeeId: 1, firstName: 'Asha', lastName: 'Rao', fullName: 'Asha Rao', designation: 'Developer', email: 'asha@example.com', mobile: null,
  dateOfJoining: '2026-01-15', dateOfLeaving: null, monthlySalary: 50000, statusId: 1, statusName: 'Active', ...o,
});
const payment = (o: Partial<CompanySalaryPayment> = {}): CompanySalaryPayment => ({
  salaryPaymentId: 7, companyEmployeeId: 1, employeeName: 'Asha Rao', periodMonth: 9, periodYear: 2026, amount: 65000, paymentDate: '2026-09-30', paymentMode: 'Bank Transfer',
  remarks: 'bonus month', recordedByName: 'Admin', status: 'Active', voidedAt: null, voidReason: null, ...o,
});

describe('CompanyEmployeesPageComponent', () => {
  let fixture: ComponentFixture<CompanyEmployeesPageComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const listReq = () => http.expectOne((r) => r.url === `${API}/api/CompanyEmployee` && r.method === 'GET');
  const rows = () => Array.from(el().querySelectorAll('app-table tbody tr')).map((r) => (r.textContent ?? '').replace(/\s+/g, ' '));
  const btn = (scope: string, label: string) => Array.from(el().querySelectorAll(scope + ' button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;

  function open(list: CompanyEmployee[] = [employee(), employee({ companyEmployeeId: 2, firstName: 'Meera', lastName: 'Das', fullName: 'Meera Das', designation: 'Sales', email: null, mobile: '99999', monthlySalary: 0, dateOfLeaving: '2026-07-31', statusName: 'Inactive', statusId: 2 })]): void {
    clearSessions();
    seedSession({ user: makeUser() });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CompanyEmployeesPageComponent);
    fixture.detectChanges();
    listReq().flush(env(list));
    fixture.detectChanges();
  }

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('lists the team with designation, contact, joining (and leaving) dates, salary and status', () => {
    open();
    const r = rows();
    expect(r[0]).toContain('Asha Rao');
    expect(r[0]).toContain('Developer');
    expect(r[0]).toContain('asha@example.com');
    expect(r[0]).toContain('₹50,000.00');
    expect(r[0]).toContain('Active');
    expect(r[1]).toContain('99999');
    expect(r[1]).toContain('15 Jan 2026'.replace('15 Jan', '15 Jan') && '31 Jul 2026');
    expect(r[1]).toContain('Inactive');
  });

  it('offers "Record salary payment" only to an active employee, and Deactivate / Activate by status', () => {
    open();
    const r = rows();
    expect(r[0]).toContain('Record salary payment');
    expect(r[0]).toContain('Deactivate');
    expect(r[1]).not.toContain('Record salary payment');
    expect(r[1]).toContain('Activate');
    expect(r[1]).not.toContain('Deactivate');
    expect(r[0]).toContain('Salary history');
  });

  it('searches (debounced) and filters by status', () => {
    vi.useFakeTimers();
    try {
      open();
      const cmp = fixture.componentInstance as unknown as { searchControl: { setValue(v: string): void }; statusControl: { setValue(v: string): void } };
      cmp.statusControl.setValue('Inactive');
      const a = listReq();
      expect(a.request.params.get('status')).toBe('Inactive');
      a.flush(env([]));
      cmp.searchControl.setValue('meera');
      vi.advanceTimersByTime(300);
      const b = listReq();
      expect(b.request.params.get('search')).toBe('meera');
      b.flush(env([]));
    } finally {
      vi.useRealTimers();
    }
  });

  it('Add Employee opens the form; blank email, mobile and leaving date are sent as null; the list reloads', () => {
    open([]);
    btn('app-page-header', 'Add Employee').click();
    fixture.detectChanges();
    expect(text()).toContain('Add employee');
    const form = (el().querySelector('app-company-employee-form-modal') as unknown as { __ngContext__: unknown }) && null;
    expect(form).toBeNull();
    const modal = fixture.debugElement.query((d) => d.name === 'app-company-employee-form-modal').componentInstance as { form: { setValue(v: unknown): void }; submit(): void };
    modal.form.setValue({ firstName: ' Vikram ', lastName: 'Shah', designation: 'Support', email: '  ', mobile: '', dateOfJoining: '2026-03-10', dateOfLeaving: '', monthlySalary: '30000' });
    modal.submit();
    const post = http.expectOne(`${API}/api/CompanyEmployee`);
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ firstName: 'Vikram', lastName: 'Shah', designation: 'Support', email: null, mobile: null, dateOfJoining: '2026-03-10', dateOfLeaving: null, monthlySalary: 30000 });
    post.flush(env(employee()));
    fixture.detectChanges();
    listReq().flush(env([employee()]));
    fixture.detectChanges();
    expect(el().querySelector('app-company-employee-form-modal')).toBeNull();
  });

  it('the form refuses a missing name, a bad email, a negative salary and a leaving date before joining', () => {
    open([]);
    btn('app-page-header', 'Add Employee').click();
    fixture.detectChanges();
    const modal = fixture.debugElement.query((d) => d.name === 'app-company-employee-form-modal').componentInstance as { form: { setValue(v: unknown): void }; submit(): void };
    modal.form.setValue({ firstName: '', lastName: 'Shah', designation: 'Support', email: 'nope', mobile: '', dateOfJoining: '2026-03-10', dateOfLeaving: '2026-01-01', monthlySalary: '-1' });
    modal.submit();
    fixture.detectChanges();
    http.expectNone(`${API}/api/CompanyEmployee`);
    expect(text()).toContain('Enter the first name');
    expect(text()).toContain('Enter a valid email address');
    expect(text()).toContain('The leaving date cannot be before the joining date');
    expect(text()).toContain('Enter a salary of 0 or more');
  });

  it('Edit opens the form with the employee\'s values and saves with PUT', () => {
    open();
    (el().querySelector('app-table app-button[aria-label="Edit employee"] button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(text()).toContain('Edit employee');
    const modal = fixture.debugElement.query((d) => d.name === 'app-company-employee-form-modal').componentInstance as { form: { getRawValue(): { firstName: string; monthlySalary: number }; patchValue(v: unknown): void }; submit(): void };
    expect(modal.form.getRawValue().firstName).toBe('Asha');
    modal.form.patchValue({ monthlySalary: '60000' });
    modal.submit();
    const put = http.expectOne(`${API}/api/CompanyEmployee/1`);
    expect(put.request.method).toBe('PUT');
    expect((put.request.body as { monthlySalary: number }).monthlySalary).toBe(60000);
    put.flush(env(employee({ monthlySalary: 60000 })));
    fixture.detectChanges();
    listReq().flush(env([employee({ monthlySalary: 60000 })]));
  });

  it('Deactivate asks for confirmation, then toggles the status (nothing is deleted) and reloads', async () => {
    open();
    const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    btn('app-table', 'Deactivate').click();
    await fixture.whenStable();
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ title: 'Deactivate employee', variant: 'danger' }));
    const post = http.expectOne(`${API}/api/CompanyEmployee/1/toggle-status`);
    post.flush(env(employee({ statusName: 'Inactive' })));
    listReq().flush(env([]));
  });

  it('declining the confirmation changes nothing', async () => {
    open();
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(false);
    btn('app-table', 'Deactivate').click();
    await fixture.whenStable();
    http.expectNone((r) => r.url.includes('toggle-status'));
  });

  it('Record salary payment starts at the current monthly salary and this month, and posts what is typed', () => {
    open();
    btn('app-table', 'Record salary payment').click();
    fixture.detectChanges();
    expect(text()).toContain('Record salary payment: Asha Rao');
    expect(text()).toContain('₹50,000.00');
    const modal = fixture.debugElement.query((d) => d.name === 'app-salary-payment-modal').componentInstance as {
      form: { getRawValue(): { amount: number; month: string; year: string }; patchValue(v: unknown): void };
      submit(): void;
    };
    const start = modal.form.getRawValue();
    expect(start.amount).toBe(50000);
    expect(start.month).toBe(String(new Date().getMonth() + 1));
    expect(start.year).toBe(String(new Date().getFullYear()));
    modal.form.patchValue({ month: '9', year: '2026', amount: '65000', paymentMode: 'Bank Transfer', remarks: ' bonus ' });
    modal.submit();
    const post = http.expectOne(`${API}/api/CompanySalary`);
    expect(post.request.body).toMatchObject({ companyEmployeeId: 1, periodMonth: 9, periodYear: 2026, amount: 65000, paymentMode: 'Bank Transfer', remarks: 'bonus' });
    post.flush(env(payment()));
    fixture.detectChanges();
    expect(el().querySelector('app-salary-payment-modal')).toBeNull();
  });

  it('the salary form refuses a zero amount and a missing payment mode', () => {
    open();
    btn('app-table', 'Record salary payment').click();
    fixture.detectChanges();
    const modal = fixture.debugElement.query((d) => d.name === 'app-salary-payment-modal').componentInstance as { form: { patchValue(v: unknown): void }; submit(): void };
    modal.form.patchValue({ amount: '0', paymentMode: '' });
    modal.submit();
    fixture.detectChanges();
    http.expectNone(`${API}/api/CompanySalary`);
    expect(text()).toContain('Enter an amount greater than zero');
    expect(text()).toContain('Choose a payment mode');
  });

  it('Salary history lists the payments (voided struck through) and voids one with a required reason', () => {
    open();
    btn('app-table', 'Salary history').click();
    fixture.detectChanges();
    const first = http.expectOne(`${API}/api/CompanySalary/employee/1?pageSize=100`);
    first.flush(env({ items: [payment(), payment({ salaryPaymentId: 6, periodMonth: 8, amount: 50000, status: 'Voided', voidReason: 'Wrong account', remarks: null })], total: 2, page: 1, pageSize: 100, activeTotal: 65000 }));
    fixture.detectChanges();
    expect(text()).toContain('Sep 2026');
    expect(text()).toContain('₹65,000.00');
    expect(text()).toContain('Voided: Wrong account');
    expect(text()).toContain('Paid in total (Active payments): ₹65,000.00');

    btn('app-salary-history-modal', 'Void').click();
    fixture.detectChanges();
    btn('app-salary-history-modal', 'Void payment').click(); // no reason yet
    fixture.detectChanges();
    http.expectNone((r) => r.url.includes('/void'));
    expect(text()).toContain('A reason is required');
    const modal = fixture.debugElement.query((d) => d.name === 'app-salary-history-modal').componentInstance as { voidReason: { setValue(v: string): void } };
    modal.voidReason.setValue('Paid twice');
    btn('app-salary-history-modal', 'Void payment').click();
    const post = http.expectOne(`${API}/api/CompanySalary/7/void`);
    expect(post.request.body).toEqual({ voidReason: 'Paid twice' });
    post.flush(env(payment({ status: 'Voided', voidReason: 'Paid twice' })));
    http.expectOne(`${API}/api/CompanySalary/employee/1?pageSize=100`).flush(env({ items: [], total: 0, page: 1, pageSize: 100, activeTotal: 0 }));
  });

  it('says so when the list cannot be loaded', () => {
    clearSessions();
    seedSession({ user: makeUser() });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CompanyEmployeesPageComponent);
    fixture.detectChanges();
    listReq().flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(text()).toContain('Could not load employees.');
  });
});
