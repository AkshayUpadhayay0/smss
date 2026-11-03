import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { MyStudentsComponent } from './my-students.component';

const URL = `${environment.apiUrl}/api/Student/my-class`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const stu = (id: string, name: string, roll: string) => ({ studentId: id, admissionNumber: 'ADM' + id, fullName: name, rollNumber: roll, genderName: 'Female', statusName: 'Active', profilePhotoUrl: null });
const DATA = {
  academicYearId: 40, academicYearName: '2026-27',
  sections: [
    { classId: 1, className: 'Nursery', sectionId: 38, sectionName: 'A', isClassTeacher: true, subjects: ['EVS', 'Rhymes & Stories'], students: [stu('S1', 'Aanya Thapa', '1'), stu('S2', 'Harsh Dobhal', '2')] },
    { classId: 6, className: 'Class 3', sectionId: 41, sectionName: 'A', isClassTeacher: false, subjects: ['Mathematics'], students: [stu('S9', 'Riya Bisht', '1')] },
  ],
};

describe('MyStudentsComponent', () => {
  let fixture: ComponentFixture<MyStudentsComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';

  function open(respond: (r: ReturnType<HttpTestingController['expectOne']>) => void) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Teacher'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyStudentsComponent);
    fixture.detectChanges();
    respond(http.expectOne(URL));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('makes one request to the self-scoped my-class endpoint (no school or section id) and lists each section with its students', () => {
    open((r) => r.flush(env(DATA)));
    const titles = [...el().querySelectorAll('.head h2')].map((h) => h.textContent?.trim());
    expect(titles).toEqual(['Nursery – A', 'Class 3 – A']);
    expect(text()).toContain('Aanya Thapa');
    expect(text()).toContain('Harsh Dobhal');
    expect(text()).toContain('Riya Bisht');
    expect(text()).toContain('2 students');
    expect(text()).toContain('1 student');
    expect(text()).toContain('2026-27');
  });

  it('marks the class-teacher section and names the subjects taught', () => {
    open((r) => r.flush(env(DATA)));
    const heads = [...el().querySelectorAll('.head')];
    expect(heads[0].textContent).toContain('Class teacher');
    expect(heads[0].textContent).toContain('Teaching EVS, Rhymes & Stories');
    expect(heads[1].textContent).not.toContain('Class teacher');
    expect(heads[1].textContent).toContain('Teaching Mathematics');
  });

  it('is read-only: only View buttons, nothing about fees, logins, photos or editing', () => {
    open((r) => r.flush(env(DATA)));
    const labels = [...el().querySelectorAll('button')].map((b) => b.textContent?.trim());
    expect(labels.every((l) => l === 'View')).toBe(true);
    expect(labels).toHaveLength(3);
    for (const w of ['Fee', 'Login', 'Edit', 'Photo', 'Aadhaar']) expect(text()).not.toContain(w);
  });

  it('View opens the restricted profile at /my-students/:studentId', () => {
    open((r) => r.flush(env(DATA)));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    ([...el().querySelectorAll('button')][0] as HTMLButtonElement).click();
    expect(navigate).toHaveBeenCalledWith(['/my-students', 'S1']);
  });

  it('says so when the teacher has no class and no timetable', () => {
    open((r) => r.flush(env({ academicYearId: 40, academicYearName: '2026-27', sections: [] })));
    expect(text()).toContain('not a class teacher or timetabled');
    expect(el().querySelector('app-table')).toBeNull();
  });

  it('says the students could not be loaded, with a retry', () => {
    open((r) => r.flush({ status: false, statusCode: 409, message: 'No academic year covers today.', data: null }, { status: 409, statusText: 'Conflict' }));
    expect(text()).toContain('Your students could not be loaded.');
    const retry = [...el().querySelectorAll('button')].find((b) => b.textContent?.includes('Try again')) as HTMLButtonElement;
    retry.click();
    http.expectOne(URL).flush(env(DATA));
    fixture.detectChanges();
    expect(text()).toContain('Aanya Thapa');
  });
});
