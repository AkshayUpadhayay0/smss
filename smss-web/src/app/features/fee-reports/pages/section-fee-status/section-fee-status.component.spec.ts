import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { SectionFeeGrid, SectionFeeStatus, SectionFeeStudent } from '../../models/section-fee-status.model';
import { SectionFeeStatusComponent } from './section-fee-status.component';

const API = environment.apiUrl;
const STATUS_URL = `${API}/api/Fee/section-status`;
const GRID_URL = `${API}/api/Fee/section-status/months`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const MY = { today: '2026-11-05', academicYearId: 40, academicYearName: '2026-27', sections: [{ classId: 34, className: 'Nursery', sectionId: 38, sectionName: 'A', rosterCount: 5, markedToday: false }, { classId: 35, className: 'LKG', sectionId: 39, sectionName: 'A', rosterCount: 3, markedToday: false }] };
const MONTHS = ['2026-04', '2026-05', '2026-10', '2026-11', '2026-12', '2027-03'].map((m) => ({ month: m, label: m, isCurrent: m === '2026-11' }));
const S = (roll: string, name: string, monthStatus: SectionFeeStudent['monthStatus'], due: number, paid: number, till: number): SectionFeeStudent =>
  ({ rollNumber: roll, fullName: name, monthStatus, dueThisMonth: due, paidThisMonth: paid, balanceThisMonth: due - paid, totalOutstandingTillDate: till });
const DATA: SectionFeeStatus = {
  sectionId: 38, className: 'Nursery', sectionName: 'A', academicYearId: 40, academicYearName: '2026-27', month: '2026-11', monthLabel: 'Nov 2026', monthInAcademicYear: true, isCurrentMonth: true, months: MONTHS,
  summary: { students: 5, paid: 1, partial: 1, unpaid: 1, overdue: 1, notDueYet: 0, noFeeThisMonth: 1, noFeeAssigned: 0, dueTotal: 12000, collectedTotal: 5000 },
  students: [
    S('1', 'Aanya Thapa', 'Paid', 3000, 3000, 0),
    S('2', 'Harsh Dobhal', 'Overdue', 3000, 0, 12000),
    S('3', 'Pari Rana', 'Partially paid', 3000, 2000, 4000),
    S('4', 'Rohan, "Ro" Bhatt', 'Unpaid', 3000, 0, 3000),
    S('5', 'Kavya Gusain', 'No fee this month', 0, 0, 8000),
  ],
};
const GRID: SectionFeeGrid = {
  sectionId: 38, className: 'Nursery', sectionName: 'A', academicYearId: 40, academicYearName: '2026-27', months: MONTHS,
  students: [{ rollNumber: '1', fullName: 'Aanya Thapa', cells: ['P', 'P', 'P', 'Partial', 'N', '-'] }, { rollNumber: '2', fullName: 'Harsh Dobhal', cells: ['O', 'O', 'U', 'O', 'N', 'N'] }],
};

describe('SectionFeeStatusComponent (month-wise)', () => {
  let fixture: ComponentFixture<SectionFeeStatusComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const names = () => [...el().querySelectorAll('.list tbody td.name')].map((c) => c.textContent?.trim());

  function open(sections: unknown = MY, status: SectionFeeStatus | null = DATA) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Teacher'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SectionFeeStatusComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Attendance/my-sections`).flush(env(sections));
    fixture.detectChanges();
    if (status) {
      const r = http.expectOne((x) => x.url === STATUS_URL);
      expect(r.request.params.has('month')).toBe(false);           // first load: the server picks the current month
      r.flush(env(status));
      fixture.detectChanges();
    }
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('shows the selected month in the picker, with this month marked', () => {
    open();
    expect(cmp()['monthControl'].value).toBe('2026-11');
    expect(cmp()['monthOptions']().find((o: { value: string }) => o.value === '2026-11').label).toContain('this month');
  });

  it('summary cards are for the selected month, each with a word AND a mark', () => {
    open();
    const tiles = [...el().querySelectorAll('.tile')].map((t) => (t.textContent ?? '').replace(/\s+/g, ' ').trim());
    expect(tiles.slice(0, 6)).toEqual(['1✓ Paid', '1◐ Partially paid', '1○ Unpaid', '1! Overdue', '0… Not due yet', '1– No fee this month']);
    expect(tiles[6]).toContain('₹5,000');
    expect(tiles[6]).toContain('₹12,000');
    expect(tiles[6]).toContain('Nov 2026');
  });

  it('table columns, and unpaid / overdue first then balance descending', () => {
    open();
    expect([...el().querySelectorAll('.list th')].map((h) => h.textContent?.trim())).toEqual(['Roll', 'Student', "This month's status", 'Due this month', 'Paid', 'Balance this month', 'Total outstanding till date']);
    // owes first (balance 3000, 3000, 1000 -> Harsh / Rohan tie broken by carry-forward, then Pari), then the rest by what they still carry
    expect(names()).toEqual(['Harsh Dobhal', 'Rohan, "Ro" Bhatt', 'Pari Rana', 'Kavya Gusain', 'Aanya Thapa']);
  });

  it('every status shows its words as well as colour; "No fee this month" shows dashes but keeps the carry-forward', () => {
    open();
    const rows = [...el().querySelectorAll('.list tbody tr')];
    expect(rows[0].querySelector('.pill')?.textContent?.trim()).toBe('! Overdue');
    const last = rows[3];
    expect(last.querySelector('.pill')?.textContent?.trim()).toBe('– No fee this month');
    const cells = [...last.querySelectorAll('td.num')].map((c) => c.textContent?.trim());
    expect(cells).toEqual(['–', '–', '–', '₹8,000.00']);
  });

  it('filters by status and by name / roll number', () => {
    open();
    cmp()['statusControl'].setValue('Overdue');
    fixture.detectChanges();
    expect(names()).toEqual(['Harsh Dobhal']);
    cmp()['statusControl'].setValue('All');
    const input = el().querySelector('input[type=search]') as HTMLInputElement;
    input.value = 'pari'; input.dispatchEvent(new Event('input')); fixture.detectChanges();
    expect(names()).toEqual(['Pari Rana']);
    input.value = 'zzz'; input.dispatchEvent(new Event('input')); fixture.detectChanges();
    expect(text()).toContain('No student matches the filter.');
  });

  it('the arrows and the dropdown ask for that month; the first and last month disable their arrow', () => {
    open();
    expect((el().querySelector('button[aria-label="Previous month"]') as HTMLButtonElement).disabled).toBe(false);
    (el().querySelector('button[aria-label="Previous month"]') as HTMLButtonElement).click();
    let r = http.expectOne((x) => x.url === STATUS_URL);
    expect(r.request.params.get('month')).toBe('2026-10');
    r.flush(env({ ...DATA, month: '2026-10', monthLabel: 'Oct 2026', isCurrentMonth: false }));
    fixture.detectChanges();
    expect(cmp()['monthControl'].value).toBe('2026-10');
    (el().querySelector('button[aria-label="Next month"]') as HTMLButtonElement).click();
    r = http.expectOne((x) => x.url === STATUS_URL);
    expect(r.request.params.get('month')).toBe('2026-11');
    r.flush(env(DATA));
    cmp()['monthControl'].setValue('2027-03');
    r = http.expectOne((x) => x.url === STATUS_URL);
    expect(r.request.params.get('month')).toBe('2027-03');
    r.flush(env({ ...DATA, month: '2027-03', monthLabel: 'Mar 2027', isCurrentMonth: false }));
    fixture.detectChanges();
    expect((el().querySelector('button[aria-label="Next month"]') as HTMLButtonElement).disabled).toBe(true);
  });

  it('changing the section asks for the server default month again', () => {
    open();
    cmp()['sectionControl'].setValue('39');
    const r = http.expectOne((x) => x.url === STATUS_URL);
    expect(r.request.params.get('sectionId')).toBe('39');
    expect(r.request.params.has('month')).toBe(false);
    r.flush(env({ ...DATA, sectionId: 39 }));
  });

  it('a month outside the academic year shows an empty state, not a table', () => {
    open(MY, { ...DATA, monthInAcademicYear: false, students: [], monthLabel: 'Jan 2025' });
    expect(text()).toContain('Jan 2025 is outside the academic year');
    expect(el().querySelector('.list')).toBeNull();
  });

  it('no fee structure for anyone: a plain explanation instead of a table of dashes', () => {
    open(MY, { ...DATA, summary: { ...DATA.summary, noFeeAssigned: 5, noFeeThisMonth: 5 }, students: DATA.students.map((s) => ({ ...s, monthStatus: 'No fee this month' as const })) });
    expect(text()).toContain('No fee structure has been assigned');
    expect(el().querySelector('.list')).toBeNull();
  });

  it('is read-only: no receipts, modes, concessions, pay buttons or inputs in the table', () => {
    open();
    expect(text()).not.toMatch(/receipt|payment mode|concession|discount|collect payment|pay now|void/i);
    expect(el().querySelectorAll('tbody input, tbody button').length).toBe(0);
  });

  it('Year grid: loads once, shows students x months with a mark and a name for every cell, current month highlighted', () => {
    open();
    (el().querySelectorAll('.view-switch button')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    http.expectOne((x) => x.url === GRID_URL).flush(env(GRID));
    fixture.detectChanges();
    expect(el().querySelectorAll('.year thead th').length).toBe(2 + 6);
    const first = el().querySelectorAll('.year tbody tr')[0];
    const dots = [...first.querySelectorAll('.dot')];
    expect(dots.map((d) => d.textContent?.trim())).toEqual(['✓', '✓', '✓', '◐', '…', '–']);
    expect(dots.map((d) => d.getAttribute('aria-label'))).toEqual(['2026-04: Paid', '2026-05: Paid', '2026-10: Paid', '2026-11: Partially paid', '2026-12: Not due yet', '2027-03: No fee this month']);
    expect(el().querySelectorAll('.year th.current').length).toBe(1);
    expect(el().querySelectorAll('.year tbody td.current').length).toBe(2);
    expect(first.querySelector('td.sticky.name')?.textContent).toContain('Aanya Thapa');
    expect(text()).not.toMatch(/₹/);                       // statuses only, no amounts
    (el().querySelectorAll('.view-switch button')[0] as HTMLButtonElement).click();
    (el().querySelectorAll('.view-switch button')[1] as HTMLButtonElement).click();
    http.expectNone((x) => x.url === GRID_URL);              // cached while the section does not change
  });

  it('a failed grid shows one calm message', () => {
    open();
    (el().querySelectorAll('.view-switch button')[1] as HTMLButtonElement).click();
    http.expectOne((x) => x.url === GRID_URL).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(el().querySelector('[role="alert"]')?.textContent).toContain('year grid could not be loaded');
  });

  it('a teacher who is not a class teacher sees a clear empty state, not an error', () => {
    open({ ...MY, sections: [] }, null);
    expect(text()).toContain('You are not the class teacher of any section');
    expect(el().querySelector('[role="alert"]')).toBeNull();
  });

  it('a failed load shows one calm message with a retry', () => {
    open(MY, null);
    http.expectOne((x) => x.url === STATUS_URL).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(el().querySelector('[role="alert"]')?.textContent).toContain('could not be loaded');
  });

  it('CSV writes the selected month as shown (filter and order), one file named with the month', () => {
    open();
    const names: string[] = [];
    let csv = '';
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) { names.push(this.download); });
    vi.spyOn(URL, 'createObjectURL').mockImplementation((b: Blob | MediaSource) => { void (b as Blob).text().then((t) => (csv = t)); return 'blob:x'; });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    cmp()['statusControl'].setValue('Unpaid');
    cmp()['exportCsv']();
    expect(names).toEqual(['fee-status-Nursery-A-2026-11.csv']);
    return new Promise<void>((res) => setTimeout(() => {
      const lines = csv.replace('\uFEFF', '').trimEnd().split('\r\n');
      expect(lines[0]).toBe('Roll,Student,Month,Status,Due this month,Paid this month,Balance this month,Total outstanding till date');
      expect(lines[1]).toBe('4,"Rohan, ""Ro"" Bhatt",Nov 2026,Unpaid,3000,0,3000,3000');
      expect(lines).toHaveLength(2);
      res();
    }, 10));
  });
});
