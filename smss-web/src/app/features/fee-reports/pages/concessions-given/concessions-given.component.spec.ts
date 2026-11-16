import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { ConcessionGivenRow, ConcessionsGiven } from '../../models/fee-report.model';
import { ConcessionsGivenComponent } from './concessions-given.component';

const API = `${environment.apiUrl}/api/FeeReports/concessions-given`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const row = (id: number, over: Partial<ConcessionGivenRow> = {}): ConcessionGivenRow => ({
  paymentId: id, receiptNumber: `RCP-2026-${String(id).padStart(4, '0')}`, paymentDate: '2026-11-10T08:30:00Z', studentId: 'GHS001ST001', studentName: 'Hiral Malik',
  concessionName: 'Sibling 10%', concessionCode: 'SIB10', concessionType: 'PERCENTAGE', concessionValue: 10, concessionAmount: 100, reason: 'second child', appliedByName: 'Priya', status: 'Active', ...over,
});
const page = (items: ConcessionGivenRow[], total = items.length, totalAmount = 0): ConcessionsGiven => ({ items, page: 1, pageSize: 25, totalItems: total, totalAmount });

describe('ConcessionsGivenComponent', () => {
  let fixture: ComponentFixture<ConcessionsGivenComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const reqs = () => http.match((r) => r.url === API);

  function open(roles: string[] = ['School Admin'], data: ConcessionsGiven | 'fail' = page([row(2), row(1, { status: 'Voided', concessionAmount: 50, reason: null })], 2, 100)) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'GHS261103002', roles }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ConcessionsGivenComponent);
    fixture.detectChanges();
    const [first] = reqs();
    if (data === 'fail') first.flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    else first.flush(env(data));
    fixture.detectChanges();
    return first;
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('opens on the current month, page 1, and lists date, receipt, student, concession, amount, reason, who applied it and the status', () => {
    const first = open();
    const now = new Date();
    const ym = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
    expect(first.request.params.get('from')).toBe(`${ym}-01`);
    expect(first.request.params.get('page')).toBe('1');
    expect(first.request.params.get('pageSize')).toBe('25');
    for (const t of ['RCP-2026-0002', 'Hiral Malik (GHS001ST001)', 'Sibling 10%', '₹100.00', 'second child', 'Priya', 'Active', 'Voided']) expect(text()).toContain(t);
    expect(text()).toContain('2 payments');
    expect(text()).toContain('Total given ₹100.00');                      // the server's total: the voided one is not in it
  });

  it('is read-only for every viewer, the Principal included: no edit, delete or add control', () => {
    for (const role of ['School Admin', 'Accountant', 'Principal']) {
      TestBed.resetTestingModule();
      open([role]);
      const buttons = [...el().querySelectorAll('button')].map((b) => b.textContent?.trim() ?? '');
      expect(buttons.some((b) => /edit|delete|add|void|remove/i.test(b))).toBe(false);
      http.verify();
    }
  });

  it('a date range reloads from page 1 with from / to; "All dates" drops them; an end before the start is refused', () => {
    open();
    cmp()['fromControl'].setValue('2026-10-01');
    cmp()['toControl'].setValue('2026-10-31');
    cmp()['page'].set(3);
    cmp()['apply']();
    let [r] = reqs();
    expect([r.request.params.get('from'), r.request.params.get('to'), r.request.params.get('page')]).toEqual(['2026-10-01', '2026-10-31', '1']);
    r.flush(env(page([])));
    fixture.detectChanges();
    expect(text()).toContain('No concessions were given in this period.');

    cmp()['toControl'].setValue('2026-09-01');
    fixture.detectChanges();
    expect(text()).toContain("The end date can't be before the start date");
    cmp()['apply']();
    expect(reqs().length).toBe(0);                                       // nothing was asked

    cmp()['clearDates']();
    [r] = reqs();
    expect(r.request.params.has('from')).toBe(false);
    expect(r.request.params.has('to')).toBe(false);
    r.flush(env(page([row(1)], 1, 100)));
  });

  it('clicking the real "Show" button (and pressing Enter in a date field) sends the request: the form submit really fires', () => {
    open();
    cmp()['fromControl'].setValue('2026-10-01');
    cmp()['toControl'].setValue('2026-10-31');
    fixture.detectChanges();
    const show = [...el().querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim() === 'Show')!;
    expect(show.disabled).toBe(false);
    show.click();                                   // a real click on the submit button, not a call to apply()
    fixture.detectChanges();
    const [r] = reqs();
    expect(r, 'clicking Show must ask the server').toBeDefined();
    expect([r.request.params.get('from'), r.request.params.get('to'), r.request.params.get('page')]).toEqual(['2026-10-01', '2026-10-31', '1']);
    r.flush(env(page([row(1)], 1, 100)));

    // Enter inside a field submits the same form
    const form = el().querySelector<HTMLFormElement>('form[aria-label="Date range"]')!;
    form.dispatchEvent(new Event('submit', { cancelable: true }));
    const again = reqs();                          // (reqs() consumes what it matches: call it once)
    expect(again.length).toBe(1);
    again[0].flush(env(page([row(1)], 1, 100)));

    // a bad range disables the button: nothing is sent
    cmp()['toControl'].setValue('2026-09-01');
    fixture.detectChanges();
    expect(show.disabled).toBe(true);
    show.click();
    expect(reqs().length).toBe(0);
  });

  it('paging asks the server for that page and size', () => {
    open(['School Admin'], page([row(1)], 60, 100));
    cmp()['onPage'](2);
    let [r] = reqs();
    expect([r.request.params.get('page'), r.request.params.get('pageSize')]).toEqual(['2', '25']);
    r.flush(env({ ...page([row(26)], 60, 100), page: 2 }));
    cmp()['onPageSize'](50);
    [r] = reqs();
    expect([r.request.params.get('page'), r.request.params.get('pageSize')]).toEqual(['1', '50']);
    r.flush(env(page([row(1)], 60, 100)));
  });

  it('shows an error state with Try again when loading fails', () => {
    open(['School Admin'], 'fail');
    expect(text()).toContain('Could not load the concessions given.');
    expect(text()).toContain('Try again');
    cmp()['load']();
    reqs()[0].flush(env(page([row(1)], 1, 100)));
    fixture.detectChanges();
    expect(text()).toContain('RCP-2026-0001');
  });

  it('exports every row of the range (all pages) as CSV, defusing a spreadsheet formula in the reason, and is disabled when empty', async () => {
    open(['School Admin'], page([row(1)], 101, 100));
    const created: string[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((b: Blob | MediaSource) => { void (b as Blob).text().then((t) => created.push(t)); return 'blob:x'; });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);

    const done = cmp()['exportCsv']();
    await Promise.resolve();
    let [r] = reqs();
    expect([r.request.params.get('page'), r.request.params.get('pageSize')]).toEqual(['1', '100']);
    r.flush(env({ ...page(Array.from({ length: 100 }, (_, i) => row(i + 1, i === 0 ? { reason: '=HYPERLINK("x")' } : {})), 101, 100) }));
    await new Promise((res) => setTimeout(res));
    [r] = reqs();
    expect(r.request.params.get('page')).toBe('2');
    r.flush(env({ ...page([row(101)], 101, 100), page: 2 }));
    await done;
    await new Promise((res) => setTimeout(res));

    expect(created.length).toBe(1);
    const csv = created[0];
    expect(csv.split('\r\n').length).toBe(1 + 101);                      // the header and every row, not just the visible page
    expect(csv).toContain('"Reason"');
    expect(csv).toContain(`"'=HYPERLINK(""x"")"`);                       // a leading = is defused
    expect(csv).not.toContain('"=HYPERLINK');

    cmp()['total'].set(0);
    fixture.detectChanges();
    const btn = [...el().querySelectorAll('button')].find((b) => b.textContent?.includes('Export CSV')) as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });
});
