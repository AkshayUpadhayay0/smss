import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { MyTimetableComponent } from './my-timetable.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
const DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'].map((name, i) => ({ dayOfWeek: i + 1, name }));
const MINE = {
  academicYearId: 6, academicYearName: '2026-27', scope: 'teacher', employeeId: 'S001EM001', employeeName: 'Anita Staff', days: DAYS, entryCount: 1,
  rows: [{
    period: { periodId: 1, periodNumber: 1, startTime: '08:00:00', endTime: '08:45:00' },
    cells: DAYS.map((d) => ({
      dayOfWeek: d.dayOfWeek,
      entry: d.dayOfWeek === 2 ? { timetableEntryId: 1, academicYearId: 6, classId: 10, className: 'Class 1', sectionId: 2, sectionName: 'B', dayOfWeek: 2, periodId: 1, periodNumber: 1, subjectId: 7, subjectName: 'Maths', employeeId: 'S001EM001', employeeName: 'Anita Staff', roomNo: '204' } : null,
    })),
  }],
};

describe('MyTimetableComponent', () => {
  let fixture: ComponentFixture<MyTimetableComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Teacher'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyTimetableComponent);
    fixture.detectChanges();
  });
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('shows the teacher\'s own week read-only: class and section, subject, room, period time; no buttons to edit', () => {
    http.expectOne(`${API}/api/Timetable/my`).flush(env(MINE));
    fixture.detectChanges();
    expect(el().textContent).toContain('2026-27');
    expect(el().textContent).toContain('08:00 – 08:45');
    const filled = el().querySelector('.cell:not(.empty)') as HTMLElement;
    expect(filled.textContent).toContain('Maths');
    expect(filled.textContent).toContain('Class 1 – B');
    expect(filled.textContent).toContain('Room 204');
    expect(el().querySelectorAll('button.cell')).toHaveLength(0);
    expect(el().querySelectorAll('.cell.empty')).toHaveLength(5);
  });

  it('says so when the timetable cannot be loaded', () => {
    http.expectOne(`${API}/api/Timetable/my`).flush({ status: false, statusCode: 404, message: 'No employee record', data: null }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(el().textContent).toContain('could not be loaded');
  });
});
