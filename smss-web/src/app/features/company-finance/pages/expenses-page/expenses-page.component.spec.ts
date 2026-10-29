import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { CompanyExpense, CompanyPaged, ExpenseCategory } from '../../models/company-finance.model';
import { CompanyExpensesPageComponent } from './expenses-page.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const categories: ExpenseCategory[] = [
  { categoryId: 1, categoryName: 'Rent', categoryCode: 'RENT' },
  { categoryId: 2, categoryName: 'Marketing', categoryCode: 'MARKETING' },
];
const expense = (o: Partial<CompanyExpense> = {}): CompanyExpense => ({
  expenseId: 11, expenseDate: '2026-09-05', categoryId: 2, categoryName: 'Marketing', description: 'Ads', amount: 500, paymentMode: 'Card', recordedByName: 'Admin',
  status: 'Active', voidedAt: null, voidReason: null, ...o,
});
const paged = (items: CompanyExpense[], activeTotal = 500): CompanyPaged<CompanyExpense> => ({ items, total: items.length, page: 1, pageSize: 10, activeTotal });

describe('CompanyExpensesPageComponent', () => {
  let fixture: ComponentFixture<CompanyExpensesPageComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const listReq = () => http.expectOne((r) => r.url === `${API}/api/CompanyExpense` && r.method === 'GET');
  /** The newest list request; an older one still in flight must have been cancelled by it. */
  const latestListReq = () => {
    const all = http.match((r) => r.url === `${API}/api/CompanyExpense` && r.method === 'GET');
    expect(all.length).toBeGreaterThan(0);
    for (const older of all.slice(0, -1)) expect(older.cancelled).toBe(true);
    return all[all.length - 1];
  };
  const btn = (scope: string, label: string) => Array.from(el().querySelectorAll(scope + ' button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, { setValue(v: string): void; value: string }> & { page: { set(v: number): void } };

  function create(inputs: Record<string, string> = {}): void {
    clearSessions();
    seedSession({ user: makeUser() });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CompanyExpensesPageComponent);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    http.expectOne(`${API}/api/CompanyExpense/categories`).flush(env(categories));
  }

  function open(items: CompanyExpense[] = [expense()], inputs: Record<string, string> = {}, activeTotal = 500): void {
    create(inputs);
    listReq().flush(env(paged(items, activeTotal)));
    fixture.detectChanges();
  }

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('lists expenses with category, amount, mode and who recorded them, plus the active total', () => {
    open();
    const row = (el().querySelector('app-table tbody tr')?.textContent ?? '').replace(/\s+/g, ' ');
    expect(row).toContain('05 Sept 2026'.replace('Sept', 'Sept'));
    expect(row).toContain('Marketing');
    expect(row).toContain('Ads');
    expect(row).toContain('₹500.00');
    expect(row).toContain('Card');
    expect(row).toContain('Admin');
    expect(text()).toContain('Active total for these filters: ₹500.00');
  });

  it('a voided expense shows its reason and has no Void button; an active one does', () => {
    open([expense(), expense({ expenseId: 12, description: 'Trip', status: 'Voided', voidReason: 'Entered twice' })], {}, 500);
    const rows = Array.from(el().querySelectorAll('app-table tbody tr')).map((r) => (r.textContent ?? '').replace(/\s+/g, ' '));
    expect(rows[0]).toContain('Void');
    expect(rows[1]).toContain('Trip (voided: Entered twice)');
    expect(rows[1]).not.toContain(' Void ');
    expect(rows[1].trim().endsWith('Voided')).toBe(true);
  });

  it('sends the category, date and status filters, going back to page 1', () => {
    open();
    cmp()['page'].set(3);
    cmp()['categoryControl'].setValue('2');
    let r = listReq();
    expect(r.request.params.get('categoryId')).toBe('2');
    expect(r.request.params.get('page')).toBe('1');
    r.flush(env(paged([])));
    cmp()['fromControl'].setValue('2026-09-01');
    cmp()['toControl'].setValue('2026-09-30');
    latestListReq().flush(env(paged([])));
    cmp()['statusControl'].setValue('Voided');
    r = listReq();
    expect(r.request.params.get('from')).toBe('2026-09-01');
    expect(r.request.params.get('to')).toBe('2026-09-30');
    expect(r.request.params.get('status')).toBe('Voided');
    r.flush(env(paged([])));
  });

  it('does not call the API for a start date after the end date', () => {
    open();
    cmp()['fromControl'].setValue('2026-10-10'); // from alone is a valid range: it loads
    latestListReq().flush(env(paged([])));
    cmp()['toControl'].setValue('2026-10-01'); // now start > end: nothing is requested
    fixture.detectChanges();
    http.expectNone((r) => r.url === `${API}/api/CompanyExpense` && r.method === 'GET');
    expect(text()).toContain('Start is after end');
  });

  it('opened from the overview: ?from, ?to, ?categoryId and ?status pre-select the filters and the first request carries them', () => {
    create({ from: '2026-01-01', to: '2026-10-29', categoryId: '1', status: 'Active' });
    const r = listReq();
    expect(r.request.params.get('from')).toBe('2026-01-01');
    expect(r.request.params.get('to')).toBe('2026-10-29');
    expect(r.request.params.get('categoryId')).toBe('1');
    expect(r.request.params.get('status')).toBe('Active');
    r.flush(env(paged([])));
  });

  it('ignores junk in the query string', () => {
    create({ from: 'yesterday', categoryId: 'abc', status: 'x' });
    const r = listReq();
    expect(r.request.params.has('from')).toBe(false);
    expect(r.request.params.has('categoryId')).toBe(false);
    expect(r.request.params.has('status')).toBe(false);
    r.flush(env(paged([])));
  });

  it('follows the query string while the page stays open (an overview card, then the sidebar link)', () => {
    open();
    fixture.componentRef.setInput('from', '2026-04-01');
    fixture.componentRef.setInput('to', '2026-10-31');
    fixture.detectChanges();
    const filtered = listReq();
    expect(filtered.request.params.get('from')).toBe('2026-04-01');
    filtered.flush(env(paged([])));
    fixture.componentRef.setInput('from', undefined);
    fixture.componentRef.setInput('to', undefined);
    fixture.detectChanges();
    const cleared = listReq();
    expect(cleared.request.params.has('from')).toBe(false);
    expect(cleared.request.params.has('to')).toBe(false);
    cleared.flush(env(paged([])));
  });

  it('Add Expense: starts on today, refuses an empty form, and posts the entered values', () => {
    open([]);
    btn('app-page-header', 'Add Expense').click();
    fixture.detectChanges();
    const modal = fixture.debugElement.query((d) => d.name === 'app-expense-form-modal').componentInstance as {
      form: { getRawValue(): { expenseDate: string }; patchValue(v: unknown): void }; submit(): void;
    };
    expect(modal.form.getRawValue().expenseDate).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    modal.submit();
    fixture.detectChanges();
    http.expectNone(`${API}/api/CompanyExpense`);
    expect(text()).toContain('Choose a category');
    expect(text()).toContain('Describe the expense');
    expect(text()).toContain('Enter an amount greater than zero');
    expect(text()).toContain('Choose a payment mode');

    modal.form.patchValue({ expenseDate: '2026-10-01', categoryId: '1', description: '  Office rent ', amount: '1000', paymentMode: 'UPI' });
    modal.submit();
    const post = http.expectOne(`${API}/api/CompanyExpense`);
    expect(post.request.body).toEqual({ expenseDate: '2026-10-01', categoryId: 1, description: 'Office rent', amount: 1000, paymentMode: 'UPI' });
    post.flush(env(expense()));
    fixture.detectChanges();
    listReq().flush(env(paged([expense()])));
    fixture.detectChanges();
    expect(el().querySelector('app-expense-form-modal')).toBeNull();
  });

  it('the category list in the form is the seeded categories', () => {
    open([]);
    btn('app-page-header', 'Add Expense').click();
    fixture.detectChanges();
    const options = (fixture.debugElement.query((d) => d.name === 'app-expense-form-modal').componentInstance as { categoryOptions(): { label: string }[] }).categoryOptions();
    expect(options.map((o) => o.label)).toEqual(['Rent', 'Marketing']);
  });

  it('Void asks for a reason (required) and posts it, then reloads', () => {
    open();
    btn('app-table', 'Void').click();
    fixture.detectChanges();
    expect(text()).toContain('Void expense');
    expect(text()).toContain('“Ads”');
    btn('app-void-reason-modal', 'Void').click(); // no reason typed
    fixture.detectChanges();
    http.expectNone((r) => r.url.includes('/void'));
    expect(text()).toContain('A reason is required');
    const modal = fixture.debugElement.query((d) => d.name === 'app-void-reason-modal').componentInstance as { reason: { setValue(v: string): void } };
    modal.reason.setValue('Entered twice');
    btn('app-void-reason-modal', 'Void').click();
    const post = http.expectOne(`${API}/api/CompanyExpense/11/void`);
    expect(post.request.body).toEqual({ voidReason: 'Entered twice' });
    post.flush(env(expense({ status: 'Voided', voidReason: 'Entered twice' })));
    fixture.detectChanges();
    listReq().flush(env(paged([expense({ status: 'Voided', voidReason: 'Entered twice' })], 0)));
    fixture.detectChanges();
    expect(el().querySelector('app-void-reason-modal')).toBeNull();
  });

  it('says so when the list cannot be loaded', () => {
    create();
    listReq().flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(text()).toContain('Could not load expenses.');
  });
});
