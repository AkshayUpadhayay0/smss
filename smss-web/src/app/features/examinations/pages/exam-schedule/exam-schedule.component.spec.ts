import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { ExamScheduleComponent } from './exam-schedule.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const SCHEME = {
  schemeId: 3, schemeName: 'Class 1 scheme', academicYearName: '2026-27', classNames: ['Class 1'], classes: [{ classId: 1, className: 'Class 1' }],
  exams: [{ examId: 10, termName: 'Term 1', examName: 'Half-Yearly', examDate: '2026-09-10', classDates: [{ classId: 1, className: 'Class 1', examDate: '2026-09-12' }] }],
};

describe('ExamScheduleComponent', () => {
  let fixture: ComponentFixture<ExamScheduleComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  function open(role: string) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: [role] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ExamScheduleComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/ReportSchedule`).flush(env([SCHEME]));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('a Teacher reads the dates and gets no inputs or Save button', () => {
    open('Teacher');
    expect(el().textContent).toContain('Half-Yearly');
    expect(el().textContent).toContain('some classes differ');
    expect(el().querySelectorAll('input[type=date]').length).toBe(0);
    expect(el().textContent).not.toContain('Save dates');
  });

  it('the School Admin edits a date and a class date, and the save body carries both', () => {
    open('School Admin');
    const inputs = el().querySelectorAll<HTMLInputElement>('input[type=date]');
    expect(inputs.length).toBe(2);
    inputs[0].value = '2026-09-15'; inputs[0].dispatchEvent(new Event('change'));
    inputs[1].value = '2026-09-16'; inputs[1].dispatchEvent(new Event('change'));
    fixture.detectChanges();
    [...el().querySelectorAll('button')].find((b) => b.textContent?.includes('Save dates'))!.click();
    const req = http.expectOne(`${API}/api/ReportSchedule`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ schemeId: 3, exams: [{ examId: 10, examDate: '2026-09-15', classDates: [{ classId: 1, examDate: '2026-09-16' }] }] });
    req.flush(env(SCHEME));
  });
});
