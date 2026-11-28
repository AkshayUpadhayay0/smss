import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { TimetableAdminComponent } from './timetable-admin.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });

const YEARS = [
  { academicYearId: 5, yearName: '2025-26', isCurrent: false, statusName: 'Active' },
  { academicYearId: 6, yearName: '2026-27', isCurrent: true, statusName: 'Active' },
];
const CLASSES = [
  { classId: 10, className: 'Class 1', sequenceOrder: 1, statusName: 'Active' },
  { classId: 11, className: 'Class 2', sequenceOrder: 2, statusName: 'Active' },
];
const sec = (sectionId: number, classId: number, sectionName: string, statusName = 'Active') => ({ sectionId, classId, className: '', sectionName, statusName });
const SECTIONS = [sec(1, 10, 'A'), sec(2, 10, 'B'), sec(3, 10, 'C', 'Inactive'), sec(4, 11, 'A')];

const entry = (dayOfWeek: number, periodId: number, subjectName: string) => ({
  timetableEntryId: 1, academicYearId: 6, classId: 10, className: 'Class 1', sectionId: 1, sectionName: 'A', dayOfWeek, periodId, periodNumber: periodId,
  subjectId: 7, subjectName, employeeId: 'S001EM001', employeeName: 'Anita Staff', roomNo: '101',
});
const DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'].map((name, i) => ({ dayOfWeek: i + 1, name }));
const GRID = (monday1: ReturnType<typeof entry> | null) => ({
  academicYearId: 6, academicYearName: '2026-27', scope: 'section', timetableId: 50, timetableName: 'Regular timetable', timetableState: 'ACTIVE', sectionId: 1, classId: 10, className: 'Class 1', sectionName: 'A', days: DAYS, entryCount: monday1 ? 1 : 0,
  rows: [1, 2].map((n) => ({
    period: { periodId: n, periodNumber: n, startTime: `0${7 + n}:00:00`, endTime: `0${7 + n}:45:00` },
    cells: DAYS.map((d) => ({ dayOfWeek: d.dayOfWeek, entry: monday1 && n === 1 && d.dayOfWeek === 1 ? monday1 : null })),
  })),
});
const VERSIONS = [{
  timetableId: 50, academicYearId: 6, classId: 10, sectionId: 1, timetableName: 'Regular timetable', scheduleId: 1, scheduleName: 'Regular', state: 'ACTIVE', isActive: true, everActive: true,
  slotCount: 1, needsTeacherCount: 0, createdAt: '2026-11-01T10:00:00Z', updatedAt: '2026-11-02T10:00:00Z', canDelete: false,
}];
const LOOKUPS = {
  subjects: [{ subjectId: 7, subjectName: 'Maths' }, { subjectId: 8, subjectName: 'English' }],
  teachers: [{ employeeId: 'S001EM001', fullName: 'Anita Staff', designationName: 'Teacher' }, { employeeId: 'S001EM002', fullName: 'Bharat Staff', designationName: 'Teacher' }],
};

describe('TimetableAdminComponent', () => {
  let fixture: ComponentFixture<TimetableAdminComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open() {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TimetableAdminComponent);
    fixture.detectChanges();
    http.match(`${API}/api/AcademicYear`).forEach((r) => r.flush(env(YEARS)));   // the page's own list and the header's viewing year both ask
    TestBed.tick();
    http.expectOne(`${API}/api/Classes`).flush(env(CLASSES));
    http.expectOne(`${API}/api/Sections`).flush(env(SECTIONS));
    fixture.detectChanges();
  }
  function pickSection(grid: unknown) {
    cmp()['classControl'].setValue('10');
    fixture.detectChanges();
    cmp()['sectionControl'].setValue('1');
    const list = http.expectOne((x) => x.url === `${API}/api/Timetables`);
    expect(list.request.params.get('sectionId')).toBe('1');
    expect(list.request.params.get('academicYearId')).toBe('6');
    list.flush(env(VERSIONS));
    http.expectOne(`${API}/api/Timetables/50/grid`).flush(env(grid));   // the ACTIVE timetable is the one in view
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('defaults to the current year, offers only the chosen class\'s ACTIVE sections, and asks for a selection first', () => {
    open();
    expect(cmp()['yearControl'].value).toBe('6');
    expect(el().textContent).toContain('Choose an academic year, a class and a section');
    cmp()['classControl'].setValue('10');
    expect(cmp()['sectionOptions']().map((o: { label: string }) => o.label)).toEqual(['A', 'B']);   // C is inactive, 2-A is another class
    http.expectNone((r) => r.url.includes('/api/Timetable'));   // also covers /api/Timetables: nothing is asked before a section is chosen
  });

  it('shows the section\'s week as a grid: one row per period, a cell per day, the filled cell with subject, teacher and room', () => {
    open();
    pickSection(GRID(entry(1, 1, 'Maths')));
    expect(el().querySelectorAll('app-timetable-grid tbody tr')).toHaveLength(2);
    expect(el().querySelectorAll('app-timetable-grid tbody td')).toHaveLength(12);
    expect([...el().querySelectorAll('app-timetable-grid thead th')].map((t) => t.textContent?.trim())).toEqual(['Period', ...DAYS.map((d) => d.name)]);
    const first = el().querySelector('app-timetable-grid tbody td button.cell') as HTMLElement;
    expect(first.textContent).toContain('Maths');
    expect(first.textContent).toContain('Anita Staff');
    expect(first.textContent).toContain('Room 101');
    expect(el().querySelectorAll('button.cell.empty')).toHaveLength(11);
  });

  it('click an empty cell -> dialog with the class\'s subjects and the teachers; Save posts the assignment and reloads the grid', () => {
    open();
    pickSection(GRID(null));
    (el().querySelectorAll('button.cell')[2] as HTMLElement).click();   // Wednesday, period 1
    fixture.detectChanges();
    http.expectOne((r) => r.url === `${API}/api/Timetable/lookups`).flush(env(LOOKUPS));
    fixture.detectChanges();
    expect(document.body.textContent).toContain('Class 1 – A · Wednesday, Period 1');
    expect(document.body.textContent).toContain('Maths');
    expect(document.body.textContent).toContain('Bharat Staff (S001EM002)');

    const slot = cmp()['slot']();
    expect(slot.current).toBeNull();
    const modal = fixture.debugElement.query((d) => d.name === 'app-slot-modal').componentInstance as Record<string, any>;
    modal['form'].patchValue({ subjectId: '8', employeeId: 'S001EM002', roomNo: ' 12 ' });
    modal['save']();
    const post = http.expectOne(`${API}/api/Timetables/50/slots/assign`);   // into the timetable in view
    expect(post.request.body).toEqual({ dayOfWeek: 3, periodId: 1, subjectId: 8, employeeId: 'S001EM002', roomNo: '12' });
    post.flush(env(entry(3, 1, 'English')));
    http.expectOne((r) => r.url === `${API}/api/Timetables`).flush(env(VERSIONS));
    http.expectOne(`${API}/api/Timetables/50/grid`).flush(env(GRID(entry(3, 1, 'English'))));
    fixture.detectChanges();
    expect(cmp()['slot']()).toBeNull();
  });

  it('the dialog will not post without a subject and a teacher', () => {
    open();
    pickSection(GRID(null));
    (el().querySelector('button.cell') as HTMLElement).click();
    fixture.detectChanges();
    http.expectOne((r) => r.url === `${API}/api/Timetable/lookups`).flush(env(LOOKUPS));
    const modal = fixture.debugElement.query((d) => d.name === 'app-slot-modal').componentInstance as Record<string, any>;
    modal['save']();
    http.expectNone((r) => r.url.includes('/slots/assign'));
  });

  it('click a filled cell -> the dialog starts on its values and "Clear slot" posts the unassign', () => {
    open();
    pickSection(GRID(entry(1, 1, 'Maths')));
    (el().querySelector('button.cell') as HTMLElement).click();
    fixture.detectChanges();
    http.expectOne((r) => r.url === `${API}/api/Timetable/lookups`).flush(env(LOOKUPS));
    const modal = fixture.debugElement.query((d) => d.name === 'app-slot-modal').componentInstance as Record<string, any>;
    expect(modal['form'].getRawValue()).toEqual({ subjectId: '7', employeeId: 'S001EM001', roomNo: '101' });
    modal['clear']();
    const post = http.expectOne(`${API}/api/Timetables/50/slots/unassign`);
    expect(post.request.body).toEqual({ dayOfWeek: 1, periodId: 1 });
    post.flush(env(entry(1, 1, 'Maths')));
    http.expectOne((r) => r.url === `${API}/api/Timetables`).flush(env(VERSIONS));
    http.expectOne(`${API}/api/Timetables/50/grid`).flush(env(GRID(null)));
  });

  it('with no periods set up it points to Periods instead of drawing an empty grid', () => {
    open();
    pickSection({ ...GRID(null), rows: [] });
    expect(el().textContent).toContain('No periods are set up yet');
    expect(el().querySelector('app-timetable-grid table')).toBeNull();
  });
});
