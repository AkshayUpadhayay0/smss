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

  describe('Print ID Cards', () => {
    const ref = (n: number, reason: string | null = null) => ({ studentId: `S${n}`, admissionNumber: `ADM${n}`, fullName: `Student ${n}`, rollNumber: String(n), reason });
    const RESULT = {
      fileName: 'IDCards_Nursery_A_2026-27.pdf', contentType: 'application/pdf', pdfBase64: 'JVBERi0=', cardCount: 12, sheetCount: 2, maxCardsPerRequest: 100,
      academicYearName: '2026-27', noPhoto: [ref(1), ref(2)], excluded: [ref(3, 'The student is inactive')],
    };
    const open = () => {
      create();
      cmp()['classControl'].setValue('4');
      cmp()['sectionControl'].setValue('13');
      studentRequests().forEach((r) => r.flush(env(STUDENTS)));
      fixture.detectChanges();
    };
    const printButton = () => [...el().querySelectorAll('app-button')].find((b) => b.textContent?.includes('Print ID Cards')) as HTMLElement | undefined;
    const idCardRequests = () => http.match((r) => r.url === `${API}/api/StudentIdCard/section/13`);
    let clicked: ReturnType<typeof vi.fn>;
    beforeEach(() => {
      URL.createObjectURL = vi.fn(() => 'blob:cards');
      URL.revokeObjectURL = vi.fn();
      clicked = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined) as unknown as ReturnType<typeof vi.fn>;
    });
    afterEach(() => vi.restoreAllMocks());

    it('is not shown until a class and section are chosen', () => {
      create();
      expect(printButton()).toBeUndefined();
    });

    it('posts for the chosen section, downloads the PDF, and lists the no-photo and excluded students', () => {
      open();
      expect(printButton()).toBeDefined();
      cmp()['printIdCards']();
      const [req] = idCardRequests();
      expect(req.request.method).toBe('POST');
      req.flush(env(RESULT));
      fixture.detectChanges();

      expect(clicked).toHaveBeenCalledTimes(1);
      const text = el().textContent ?? '';
      expect(text).toContain('12 cards on 2 sheets');
      expect(text).toContain('2 printed without a photo');
      expect(text).toContain('Student 1');
      expect(text).toContain('1 left out of the PDF');
      expect(text).toContain('The student is inactive');
    });

    it('when nobody qualifies there is no download, and the excluded list still shows', () => {
      open();
      cmp()['printIdCards']();
      idCardRequests()[0].flush(env({ ...RESULT, pdfBase64: null, cardCount: 0, sheetCount: 0, noPhoto: [] }));
      fixture.detectChanges();
      expect(clicked).not.toHaveBeenCalled();
      expect(el().textContent).toContain('Nothing to print');
      expect(el().textContent).toContain('left out of the PDF');
    });

    it('ignores a second click while generating, and re-enables afterwards', () => {
      open();
      cmp()['printIdCards']();
      cmp()['printIdCards']();
      const reqs = idCardRequests();
      expect(reqs).toHaveLength(1);
      reqs[0].flush(env({ ...RESULT, noPhoto: [], excluded: [] }));
      expect(cmp()['cardsBusy']()).toBe(false);
      fixture.detectChanges();
      expect(el().textContent).toContain('Every student has a photo and was included.');
    });

    it('a failed run (e.g. over the limit) leaves no stale report and does not download', () => {
      open();
      cmp()['printIdCards']();
      idCardRequests()[0].flush({ status: false, statusCode: 400, message: 'over the limit', data: null }, { status: 400, statusText: 'Bad Request' });
      fixture.detectChanges();
      expect(cmp()['cardResult']()).toBeNull();
      expect(clicked).not.toHaveBeenCalled();
      expect(cmp()['cardsBusy']()).toBe(false);
    });

    it('changing the section clears the previous report', () => {
      open();
      cmp()['printIdCards']();
      idCardRequests()[0].flush(env(RESULT));
      cmp()['classControl'].setValue('7');
      expect(cmp()['cardResult']()).toBeNull();
    });
  });

  it('restores the filter it was opened with (Back to Students)', () => {
    create({ academicYearId: '2', classId: '4', sectionId: '13' });
    const [req] = studentRequests();
    expect(req.request.params.get('academicYearId')).toBe('2');
    expect(req.request.params.get('classSectionId')).toBe('13');
    req.flush(env(STUDENTS));
  });
});
