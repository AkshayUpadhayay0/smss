import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { StudentListComponent } from './student-list.component';

const API = 'https://localhost:7037';
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const YEARS = [
  { academicYearId: 2, yearName: '2025-2026', isCurrent: false, statusName: 'Active' },
  { academicYearId: 3, yearName: '2026-2027', isCurrent: true, statusName: 'Active' },
];
const CLASSES = [
  { classId: 4, className: 'Nursery', statusName: 'Active' },
  { classId: 7, className: 'Class 1', statusName: 'Active' },
];
const SECTIONS = [
  { sectionId: 13, classId: 4, className: 'Nursery', sectionName: 'A', statusName: 'Active' },
  { sectionId: 14, classId: 4, className: 'Nursery', sectionName: 'Old', statusName: 'Inactive' },
  { sectionId: 20, classId: 7, className: 'Class 1', sectionName: 'B', statusName: 'Active' },
];
const STUDENTS = [
  { studentId: 'DPS001ST002', admissionNumber: 'ADMNO20260001', fullName: 'Akshay Upadhayay', rollNumber: '1', genderName: 'Male', statusName: 'Active', enrollmentId: 5, feeStatus: 'Pending' },
];

describe('StudentListComponent', () => {
  let fixture: ComponentFixture<StudentListComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>; // protected members

  function create(queryParams: Record<string, string> = {}) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: new Map(Object.entries(queryParams)) } } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StudentListComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/AcademicYear/sch2026006`.replace('/sch2026006', '')).flush(env(YEARS));
    http.expectOne(`${API}/api/Classes`).flush(env(CLASSES));
    http.expectOne(`${API}/api/Sections`).flush(env(SECTIONS));
    fixture.detectChanges();
  }

  const studentRequests = () => http.match((r) => r.url === `${API}/api/Student/sch2026006`);

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'sch2026006', roles: ['School Admin'] }) });
  });
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('defaults the year to the current one and waits for a class and section', () => {
    create();
    expect(cmp()['yearControl'].value).toBe('3');
    expect(el().textContent).toContain('Select a class and section to view students');
    expect(studentRequests()).toHaveLength(0);
  });

  it('lists only the ACTIVE sections of the chosen class', () => {
    create();
    cmp()['classControl'].setValue('4');
    fixture.detectChanges();
    expect(cmp()['sectionOptions']().map((o: { label: string }) => o.label)).toEqual(['A']);
  });

  it('loads the students once year (defaulted), class and section are all chosen', () => {
    create();
    cmp()['classControl'].setValue('4');
    cmp()['sectionControl'].setValue('13');
    fixture.detectChanges();

    const [req] = studentRequests();
    expect(req.request.params.get('academicYearId')).toBe('3'); // the DEFAULTED year counts as selected
    expect(req.request.params.get('classSectionId')).toBe('13');
    req.flush(env(STUDENTS));
    fixture.detectChanges();

    expect(el().textContent).not.toContain('Select a class and section');
    expect(el().textContent).toContain('Akshay Upadhayay');
    expect(el().textContent).toContain('ADMNO20260001');
  });

  it('changing the class clears the section and goes back to the empty state', () => {
    create();
    cmp()['classControl'].setValue('4');
    cmp()['sectionControl'].setValue('13');
    studentRequests().forEach((r) => r.flush(env(STUDENTS)));
    fixture.detectChanges();

    cmp()['classControl'].setValue('7');
    fixture.detectChanges();
    expect(cmp()['sectionControl'].value).toBe('');
    expect(el().textContent).toContain('Select a class and section to view students');
  });

  it('shows a Fee Status badge per student in the established colours, and a neutral "Not Set" when no fees are set up', () => {
    create();
    cmp()['classControl'].setValue('4');
    cmp()['sectionControl'].setValue('13');
    const base = STUDENTS[0];
    const list = (['Overdue', 'Partially Paid', 'Pending', 'Paid', 'Not Set'] as const).map((feeStatus, i) => ({ ...base, studentId: `S${i}`, admissionNumber: `A${i}`, rollNumber: String(i + 1), feeStatus }));
    studentRequests().forEach((r) => r.flush(env(list)));
    fixture.detectChanges();

    const pills = [...el().querySelectorAll('tbody .pill')].filter((p) => ['Overdue', 'Partially Paid', 'Pending', 'Paid', 'Not Set'].includes(p.textContent?.trim() ?? ''));
    const tones = Object.fromEntries(pills.map((p) => [p.textContent!.trim(), [...p.classList].find((c) => c.startsWith('tone-'))]));
    expect(tones).toEqual({ Overdue: 'tone-red', 'Partially Paid': 'tone-blue', Pending: 'tone-yellow', Paid: 'tone-green', 'Not Set': 'tone-gray' });
    expect(el().textContent).toContain('Fee Status');
  });

  it('the Fee status dropdown filters on the server: All sends no param, a status is sent and reloads the list', () => {
    create();
    cmp()['classControl'].setValue('4');
    cmp()['sectionControl'].setValue('13');
    const first = studentRequests();
    expect(first).toHaveLength(1);
    expect(first[0].request.params.has('feeStatus')).toBe(false);
    first[0].flush(env(STUDENTS));
    expect((cmp()['feeStatusOptions'] as { value: string }[]).map((o) => o.value)).toEqual(['All', 'Overdue', 'Partially Paid', 'Pending', 'Paid', 'Not Set']);

    cmp()['feeStatusControl'].setValue('Partially Paid');
    const [req] = studentRequests();
    expect(req.request.params.get('feeStatus')).toBe('Partially Paid');
    req.flush(env([]));
    fixture.detectChanges();

    cmp()['feeStatusControl'].setValue('All');
    const [again] = studentRequests();
    expect(again.request.params.has('feeStatus')).toBe(false);
    again.flush(env(STUDENTS));
  });

  it('restores the fee status it was opened with (Back to Students)', () => {
    create({ academicYearId: '2', classId: '4', sectionId: '13', feeStatus: 'Overdue' });
    const [req] = studentRequests();
    expect(req.request.params.get('feeStatus')).toBe('Overdue');
    req.flush(env(STUDENTS));
  });

  it('restores the filter it was opened with (Back to Students)', () => {
    create({ academicYearId: '2', classId: '4', sectionId: '13' });
    const [req] = studentRequests();
    expect(req.request.params.get('academicYearId')).toBe('2');
    expect(req.request.params.get('classSectionId')).toBe('13');
    req.flush(env(STUDENTS));
  });
});
