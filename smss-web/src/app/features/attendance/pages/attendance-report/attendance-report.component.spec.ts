import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { MonthlyAttendanceReport, ReportMark, ReportOptions } from '../../models/attendance-report.model';
import { AttendanceReportComponent } from './attendance-report.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const YEAR = { academicYearId: 40, yearName: '2026-27', isCurrent: true, startDate: '2026-04-01', endDate: '2027-03-31' };
const OPTIONS_ALL: ReportOptions = {
  academicYearId: 40, allSections: true, years: [YEAR, { academicYearId: 30, yearName: '2025-26', isCurrent: false, startDate: '2025-04-01', endDate: '2026-03-31' }],
  sections: [
    { classId: 34, className: 'Nursery', sectionId: 38, sectionName: 'A' },
    { classId: 35, className: 'LKG', sectionId: 39, sectionName: 'A' },
    { classId: 35, className: 'LKG', sectionId: 53, sectionName: 'B' },
  ],
};
const OPTIONS_TEACHER: ReportOptions = { ...OPTIONS_ALL, allSections: false, sections: [OPTIONS_ALL.sections[0]] };

function day(d: number, over = {}) {
  const date = `2026-11-${String(d).padStart(2, '0')}`;
  const dow = new Date(`${date}T00:00:00`).getDay() || 7;
  return { date, dayOfWeek: dow, isWorkingDay: dow !== 7, nonWorkingReason: dow === 7 ? 'Sunday' : null, holidayName: null, isFuture: d > 5, markedCount: d >= 2 && d <= 4 ? 10 : 0, ...over };
}
const DAYS = Array.from({ length: 30 }, (_, i) => day(i + 1));
const marks = (pattern: Record<number, ReportMark>): ReportMark[] => DAYS.map((d, i) => pattern[i + 1] ?? (d.isWorkingDay ? 'NotMarked' : 'NonWorking'));
const REPORT: MonthlyAttendanceReport = {
  sectionId: 38, classId: 34, className: 'Nursery', sectionName: 'A', academicYearId: 40, academicYearName: '2026-27', year: 2026, month: 11, monthName: 'November 2026', today: '2026-11-05',
  days: DAYS,
  students: [
    { studentId: 'S1', fullName: 'Aanya Thapa', rollNumber: '1', isActive: true, marks: marks({ 2: 'Present', 3: 'Present', 4: 'Present' }), presentDays: 3, absentDays: 0, notMarkedDays: 1, percentage: 100, belowThreshold: false },
    { studentId: 'S2', fullName: 'Harsh Dobhal', rollNumber: '2', isActive: true, marks: marks({ 2: 'Present', 3: 'Absent', 4: 'Absent' }), presentDays: 1, absentDays: 2, notMarkedDays: 1, percentage: 33.3, belowThreshold: true },
    { studentId: 'S3', fullName: 'Pari Rana', rollNumber: '3', isActive: false, marks: marks({ 2: 'Present', 3: 'Present', 4: 'Present' }), presentDays: 3, absentDays: 0, notMarkedDays: 1, percentage: 100, belowThreshold: false },
  ],
  summary: { studentCount: 3, averagePercentage: 77.8, studentsBelowThreshold: 1, thresholdPercentage: 75, workingDays: 4, daysNotMarked: 1, notMarkedDates: ['2026-11-05'] },
};

describe('AttendanceReportComponent', () => {
  let fixture: ComponentFixture<AttendanceReportComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(options: ReportOptions | 'fail', report: MonthlyAttendanceReport | 'fail' | null = REPORT, roles = ['School Admin']) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: roles[0] === 'Teacher' ? 'Employee' : 'School Id', roles }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AttendanceReportComponent);
    fixture.detectChanges();
    const o = http.expectOne((r) => r.url === `${API}/api/Attendance/report/options`);
    if (options === 'fail') {
      o.flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();
      return;
    }
    o.flush(env(options));
    fixture.detectChanges();
    if (report === null) return;
    const r = http.expectOne((x) => x.url === `${API}/api/Attendance/report/monthly`);
    if (report === 'fail') r.flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    else r.flush(env(report));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('loads the pickers, then the report for the first section and the current month', () => {
    open(OPTIONS_ALL, REPORT);
    expect(cmp()['yearControl'].value).toBe('40');
    expect(cmp()['sectionControl'].value).toBe('38');
    expect(text()).toContain('Nursery – A');
    expect(text()).toContain('November 2026');
  });

  it('School Admin / Principal: class picker + that class\'s sections only', () => {
    open(OPTIONS_ALL, null);
    // the very first request is for the report; answer it so verify() is clean
    http.expectOne((r) => r.url === `${API}/api/Attendance/report/monthly`).flush(env(REPORT));
    fixture.detectChanges();
    expect(cmp()['classOptions']().map((o: { label: string }) => o.label)).toEqual(['Nursery', 'LKG']);
    cmp()['classControl'].setValue('35');
    expect(cmp()['sectionOptions']().map((o: { label: string }) => o.label)).toEqual(['A', 'B']);
    expect(cmp()['sectionControl'].value).toBe('39');
    const q = http.expectOne((r) => r.url === `${API}/api/Attendance/report/monthly`);
    expect(q.request.params.get('sectionId')).toBe('39');
    q.flush(env({ ...REPORT, sectionId: 39 }));
  });

  it('a class teacher gets one "Class – Section" picker and no class picker', () => {
    open(OPTIONS_TEACHER, REPORT, ['Teacher']);
    expect(cmp()['allSections']()).toBe(false);
    const labels = [...el().querySelectorAll('label')].map((l) => l.textContent?.trim());
    expect(labels).toContain('Class and section');
    expect(labels).not.toContain('Class');
    expect(cmp()['sectionOptions']().map((o: { label: string }) => o.label)).toEqual(['Nursery – A']);
  });

  it('a teacher who is not a class teacher sees a clear empty state, not an error', () => {
    open({ ...OPTIONS_TEACHER, sections: [] }, null, ['Teacher']);
    expect(text()).toContain('You are not the class teacher of any section');
    expect(el().querySelector('[role="alert"]')).toBeNull();
    expect(el().querySelector('.grid')).toBeNull();
  });

  it('shows the summary strip: average %, students below 75%, working days, days not marked', () => {
    open(OPTIONS_ALL, REPORT);
    const tiles = [...el().querySelectorAll('.summary .tile')].map((t) => t.textContent?.replace(/\s+/g, ' ').trim());
    expect(tiles).toEqual(['77.8%Average attendance', '1Students below 75%', '4Working days so far', '1Days not marked']);   // number, then label (two stacked lines)
    expect(el().querySelectorAll('.summary .tile.warn').length).toBe(2);
  });

  it('draws one column per day, greys Sundays, and marks P / A / – compactly', () => {
    open(OPTIONS_ALL, REPORT);
    expect(el().querySelectorAll('thead th.day').length).toBe(30);
    const sunday = el().querySelectorAll('thead th.day')[0] as HTMLElement;   // 1 Nov 2026 is a Sunday
    expect(sunday.classList.contains('off')).toBe(true);
    const row2 = el().querySelectorAll('tbody tr')[1];
    const cells = [...row2.querySelectorAll('td.mk')].map((c) => c.textContent?.trim());
    expect(cells.slice(0, 5)).toEqual(['', 'P', 'A', 'A', '–']);                // Sun (blank), Mon P, Tue A, Wed A, Thu not marked
    expect(row2.querySelectorAll('td.mk.a').length).toBe(2);
    expect(cells.slice(5).every((c) => c === '')).toBe(true);                   // the rest of the month is in the future or a Sunday: blank
  });

  it('highlights a student under 75% and tags a student who has left', () => {
    open(OPTIONS_ALL, REPORT);
    const rows = el().querySelectorAll('tbody tr');
    expect(rows[1].classList.contains('low')).toBe(true);
    expect(rows[1].querySelector('td.pct')?.classList.contains('low')).toBe(true);
    expect(rows[0].classList.contains('low')).toBe(false);
    expect(rows[2].textContent).toContain('left');
  });

  it('shows per-student Present / Absent / % columns, with the name column pinned', () => {
    open(OPTIONS_ALL, REPORT);
    const tds = [...el().querySelectorAll('tbody tr')[1].querySelectorAll('td.tot')].map((t) => t.textContent?.trim());
    expect(tds).toEqual(['1', '2', '33.3']);
    expect(el().querySelectorAll('tbody tr')[1].querySelector('td.sticky.name')?.textContent).toContain('Harsh Dobhal');
  });

  it('changing the month asks for that month; changing the year reloads the sections for it', () => {
    open(OPTIONS_ALL, REPORT);
    cmp()['monthControl'].setValue('2026-10');
    const q = http.expectOne((r) => r.url === `${API}/api/Attendance/report/monthly`);
    expect([q.request.params.get('year'), q.request.params.get('month')]).toEqual(['2026', '10']);
    q.flush(env({ ...REPORT, month: 10, monthName: 'October 2026' }));
    cmp()['yearControl'].setValue('30');
    const o = http.expectOne((r) => r.url === `${API}/api/Attendance/report/options`);
    expect(o.request.params.get('academicYearId')).toBe('30');
    o.flush(env({ ...OPTIONS_ALL, academicYearId: 30 }));
    http.expectOne((r) => r.url === `${API}/api/Attendance/report/monthly`).flush(env(REPORT));
    fixture.detectChanges();
    expect(cmp()['sectionControl'].value).toBe('38');   // the chosen section survives a year change
  });

  it('a month with no students gives a plain message and disables the exports', () => {
    open(OPTIONS_ALL, { ...REPORT, students: [], summary: { ...REPORT.summary, studentCount: 0, averagePercentage: null, studentsBelowThreshold: 0 } });
    expect(text()).toContain('This section has no students for November 2026');
    expect([...el().querySelectorAll('.actions button')].every((b) => (b as HTMLButtonElement).disabled)).toBe(true);
  });

  it('a failed report shows one calm message with a retry, and failed options show a retry too', () => {
    open(OPTIONS_ALL, 'fail');
    expect(el().querySelector('[role="alert"]')?.textContent).toContain('could not be loaded');
    cmp()['loadReport']();
    http.expectOne((r) => r.url === `${API}/api/Attendance/report/monthly`).flush(env(REPORT));
    fixture.detectChanges();
    expect(el().querySelector('.grid')).not.toBeNull();
  });

  it('failed options', () => {
    open('fail');
    expect(el().querySelector('[role="alert"]')?.textContent).toContain('could not be set up');
  });

  it('CSV export saves one file with the grid; PDF export downloads from the server endpoint', () => {
    open(OPTIONS_ALL, REPORT);
    const saved: { blob: Blob; name: string }[] = [];
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) { saved.push({ blob: null as unknown as Blob, name: this.download }); });
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:x');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    cmp()['exportCsv']();
    expect(saved.at(-1)?.name).toBe('attendance-Nursery-A-2026-11.csv');
    cmp()['exportPdf']();
    const r = http.expectOne((x) => x.url.startsWith(`${API}/api/Attendance/report/monthly/pdf`));
    expect(r.request.url).toContain('sectionId=38');
    expect(r.request.url).toContain('year=2026');
    expect(r.request.url).toContain('month=11');
    expect(r.request.url).toContain('academicYearId=40');
    r.flush(new Blob(['%PDF'], { type: 'application/pdf' }));
    expect(saved.at(-1)?.name).toBe('attendance-Nursery-A-2026-11.pdf');
    click.mockRestore();
  });

  it('a PDF failure shows the server message once', () => {
    open(OPTIONS_ALL, REPORT);
    cmp()['exportPdf']();
    const r = http.expectOne((x) => x.url.startsWith(`${API}/api/Attendance/report/monthly/pdf`));
    r.flush(new Blob([JSON.stringify({ message: 'You can only see the attendance report of the section you are the class teacher of.' })], { type: 'application/json' }), { status: 403, statusText: 'Forbidden' });
    return new Promise<void>((resolve) => setTimeout(() => { fixture.detectChanges(); expect(cmp()['exporting']()).toBe(false); resolve(); }, 10));
  });
});
