import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { MyResultsComponent } from './my-results.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
const res = (assessmentId: number, title: string, testDate: string, extra = {}) => ({
  assessmentId, title, assessmentTypeName: 'Class Test', subjectName: 'Maths', testDate, maxMarks: 20, obtainedMarks: 17.5, isAbsent: false, ...extra,
});

describe('MyResultsComponent', () => {
  let fixture: ComponentFixture<MyResultsComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(list: unknown[]) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Student', roles: ['Student'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyResultsComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Assessment/student/my`).flush(env(list));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('lists the student\'s own results, most recent first, as obtained / max, with "Absent" when they missed the test', () => {
    open([res(1, 'Old quiz', '2026-09-01'), res(3, 'Absent test', '2026-10-20', { isAbsent: true, obtainedMarks: null }), res(2, 'Fractions', '2026-10-10', { subjectName: 'English', obtainedMarks: 9, maxMarks: 10 })]);
    const rows = cmp()['rows']();
    expect(rows.map((r: { title: string }) => r.title)).toEqual(['Absent test', 'Fractions', 'Old quiz']);
    expect(rows.map((r: { result: string }) => r.result)).toEqual(['Absent', '9 / 10', '17.5 / 20']);
    expect(rows[1].date).toBe('10 Oct 2026');
    expect(rows[1].typeName).toBe('Class Test');
    expect(rows[1].subjectName).toBe('English');
  });

  it('is read-only and shows nothing but the student\'s own marks: no average, rank or edit controls', () => {
    open([res(1, 'Quiz', '2026-10-10')]);
    expect(el().querySelectorAll('input, button, select, textarea')).toHaveLength(0);
    expect(el().textContent?.toLowerCase()).not.toMatch(/average|rank|class mean|position/);
  });

  it('says so when there are no results, and when they cannot be loaded', () => {
    open([]);
    expect(el().textContent).toContain('No test results yet');
  });

  it('shows an error note when the request fails', () => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Student', roles: ['Student'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyResultsComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Assessment/student/my`).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(el().textContent).toContain('could not be loaded');
  });
});
