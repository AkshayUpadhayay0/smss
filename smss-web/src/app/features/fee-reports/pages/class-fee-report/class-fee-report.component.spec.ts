import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
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

const row = (id: string, name: string, due: number, paid: number, status: string) => ({ studentId: id, fullName: name, rollNumber: null, sectionName: 'A', totalDue: due, totalCollected: paid, outstanding: due - paid, status });
const REPORT = (canManage: boolean) => ({
  academicYearId: 3, academicYearName: '2026-27', classId: 7, className: 'Class 1', sectionId: null, canManage,
  students: [row('S1', 'Asha', 30000, 30000, 'Paid'), row('S2', 'Bela', 30000, 0, 'Overdue'), row('S3', 'Chitra', 30000, 5000, 'Partially Paid'), row('S4', 'Dev', 0, 0, 'Not Set')],
  totalDue: 90000, totalCollected: 35000, totalOutstanding: 55000, statusCounts: { Overdue: 1, 'Partially Paid': 1, Pending: 0, Paid: 1, 'Not Set': 1 },
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
    http.expectOne(`${API}/api/AcademicYear`).flush(env(YEARS));
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
});
