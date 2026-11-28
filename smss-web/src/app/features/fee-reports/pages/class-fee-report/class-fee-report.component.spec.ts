import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { YearContextService } from '../../../../core/services/year-context.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const YEARS = [
  { academicYearId: 2, yearName: '2025-26', isCurrent: false, statusName: 'Active' },
  { academicYearId: 3, yearName: '2026-27', isCurrent: true, statusName: 'Active' },
];
const CLASSES = [{ classId: 7, className: 'Class 1', statusName: 'Active' }];
const SECTIONS = [{ sectionId: 20, classId: 7, className: 'Class 1', sectionName: 'A', statusName: 'Active' }];
import { ClassFeeReportComponent } from './class-fee-report.component';

const row = (id: string, name: string, due: number, paid: number, status: string, extra: Record<string, unknown> = {}) => ({ studentId: id, fullName: name, rollNumber: null, sectionName: 'A', totalDue: due, totalCollected: paid, concession: 0, outstanding: due - paid, status, ...extra });
const REPORT = (canManage: boolean) => ({
  academicYearId: 3, academicYearName: '2026-27', classId: 7, className: 'Class 1', sectionId: null, canManage,
  students: [row('S1', 'Asha', 30000, 30000, 'Paid'), row('S2', 'Bela', 30000, 0, 'Overdue'), row('S3', 'Chitra', 30000, 5000, 'Partially Paid'), row('S4', 'Dev', 0, 0, 'Not Set')],
  totalDue: 90000, totalCollected: 35000, totalConcession: 1500, totalOutstanding: 55000, statusCounts: { Overdue: 1, 'Partially Paid': 1, Pending: 0, Paid: 1, 'Not Set': 1 },
});

describe('ClassFeeReportComponent', () => {
  let fixture: ComponentFixture<ClassFeeReportComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const reportReqs = () => http.match((r) => r.url === `${API}/api/FeeReports/class/7`);
  const names = () => cmp()['sortedRows']().map((r: { fullName: string }) => r.fullName);

  function open(roles: string[], canManage: boolean, query: Record<string, string> = { academicYearId: '3', classId: '7' }) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: new Map(Object.entries(query)) } } }] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ClassFeeReportComponent);
    fixture.detectChanges();
    http.match(`${API}/api/AcademicYear`).forEach((r) => r.flush(env(YEARS)));   // the header's viewing year and this page's own option list both ask
    TestBed.tick();
    http.expectOne(`${API}/api/Classes`).flush(env(CLASSES));
    http.expectOne(`${API}/api/Sections`).flush(env(SECTIONS));
    const reqs = reportReqs();
    reqs.forEach((r) => r.flush(env(REPORT(canManage))));
    fixture.detectChanges();
    return reqs;
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('opened from the dashboard filter: loads that year + class once, who owes most first, with the class total', () => {
    const reqs = open(['School Admin'], true);
    expect(reqs).toHaveLength(1);
    expect(reqs[0].request.params.get('academicYearId')).toBe('3');
    expect(names()).toEqual(['Bela', 'Chitra', 'Asha', 'Dev']);   // outstanding, high to low
    const text = el().textContent ?? '';
    expect(text).toContain('Class total');
    expect(text).toContain('₹90,000.00');
    expect(text).toContain('₹55,000.00');
    expect(text).toContain('Overdue: 1');
    expect(text).toContain('Concession');                  // the new column and the class total
    expect(text).toContain('₹1,500.00');
  });

  it('opened with ?month=2026-11 (the dashboard Not paid card): asks for that month only and says so', () => {
    const reqs = open(['School Admin'], true, { academicYearId: '3', classId: '7', month: '2026-11' });
    expect(reqs).toHaveLength(1);
    expect(reqs[0].request.params.get('month')).toBe('2026-11');
    expect(el().querySelector('[data-month-filter]')?.textContent).toContain('November 2026');
  });

  it('opened for a month without a class (the dashboard card is school-wide): starts with the first class', () => {
    const reqs = open(['School Admin'], true, { month: '2026-11' });
    expect(reqs).toHaveLength(1);
    expect(reqs[0].request.params.get('month')).toBe('2026-11');
    expect(cmp()['classControl'].value).toBe('7');
  });

  it('ignores a month that is not yyyy-MM and sends no month at all', () => {
    for (const bad of ['2026-13', 'November', '2026-1']) {
      TestBed.resetTestingModule();
      const reqs = open(['School Admin'], true, { academicYearId: '3', classId: '7', month: bad });
      expect(reqs[0].request.params.has('month'), bad).toBe(false);
      expect(el().querySelector('[data-month-filter]'), bad).toBeNull();
      http.verify();
    }
  });

  it('"Show the whole year" drops the month and reloads without it', () => {
    open(['School Admin'], true, { academicYearId: '3', classId: '7', month: '2026-11' });
    (el().querySelector('[data-month-filter] button') as HTMLButtonElement).click();
    const again = reportReqs();
    expect(again).toHaveLength(1);
    expect(again[0].request.params.has('month')).toBe(false);
    again[0].flush(env(REPORT(true)));
    fixture.detectChanges();
    expect(el().querySelector('[data-month-filter]')).toBeNull();
  });

  it('sorts by status (worst first) and by name', () => {
    open(['School Admin'], true);
    cmp()['onSort']({ key: 'status', direction: 'asc' });
    expect(names()).toEqual(['Bela', 'Chitra', 'Asha', 'Dev']);
    cmp()['onSort']({ key: 'fullName', direction: 'asc' });
    expect(names()).toEqual(['Asha', 'Bela', 'Chitra', 'Dev']);
    cmp()['onSort']({ key: 'totalCollected', direction: 'desc' });
    expect(names()[0]).toBe('Asha');
  });

  it('a student with no fees set up shows dashes, not zero rupees', () => {
    open(['School Admin'], true);
    const dev = cmp()['sortedRows']().find((r: { fullName: string }) => r.fullName === 'Dev');
    expect(dev).toMatchObject({ totalDue: '—', outstanding: '—', status: 'Not Set' });
  });

  it('School Admin: drill-down to the student and CSV export are available', () => {
    open(['School Admin'], true);
    expect(cmp()['canDrillDown']()).toBe(true);
    expect(el().textContent).toContain('Export CSV');
  });

  it('Accountant: export and drill-down into the student Fees tab (to collect / void)', () => {
    open(['Accountant'], true);
    expect(cmp()['canDrillDown']()).toBe(true);
    expect(el().textContent).toContain('Export CSV');
  });

  it('Principal: same table, no drill-down, no export', () => {
    open(['Principal'], false);
    expect(names()).toHaveLength(4);
    expect(cmp()['canDrillDown']()).toBe(false);
    expect(el().textContent).not.toContain('Export CSV');
  });

  it('without a class in the URL it waits for one', () => {
    open(['School Admin'], true, {});
    expect(el().textContent).toContain('Select a class to see its fee report');
  });

  it('has a separate Earlier years column (not part of Outstanding) and marks a student who left but still owes', () => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: new Map(Object.entries({ academicYearId: '3', classId: '7' })) } } }] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ClassFeeReportComponent);
    fixture.detectChanges();
    http.match(`${API}/api/AcademicYear`).forEach((r) => r.flush(env(YEARS)));   // the header's viewing year and this page's own option list both ask
    TestBed.tick();
    http.expectOne(`${API}/api/Classes`).flush(env(CLASSES));
    http.expectOne(`${API}/api/Sections`).flush(env(SECTIONS));
    const withEarlier = { ...REPORT(true), students: [row('S2', 'Bela', 30000, 0, 'Overdue', { earlierYears: 500 }), row('S5', 'Esha', 1000, 0, 'Pending', { earlierYears: 200, left: true })], totalEarlierYears: 700, totalOutstanding: 31000 };
    reportReqs().forEach((r) => r.flush(env(withEarlier)));
    fixture.detectChanges();
    const text = el().textContent ?? '';
    expect(text).toContain('Earlier years');
    expect(text).toContain('₹500.00');
    expect(names()).toContain('Esha (Left)');
    expect(el().querySelector('[data-earlier-total]')?.textContent).toContain('₹700.00');
    expect(cmp()['sortedRows']().find((r: { studentId: string }) => r.studentId === 'S2').outstanding).toBe('₹30,000.00');   // the year's figure stays on its own
  });

  it('follows the header viewing year: another year asks the server for that year, with the explicit id', () => {
    open(['School Admin'], true);
    expect(el().querySelector('[data-year-note]')?.textContent).toContain('2026-27');
    TestBed.inject(YearContextService).select('2');
    TestBed.tick();
    const [r] = reportReqs();
    expect(r.request.params.get('academicYearId')).toBe('2');
    r.flush(env(REPORT(true)));
  });
});
