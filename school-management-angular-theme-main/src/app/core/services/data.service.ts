import { Injectable } from '@angular/core';
import { of, delay, Observable } from 'rxjs';
import * as M from './mock-data';
import * as Models from '../models/school.models';

/**
 * Every method below returns an Observable, mirroring what an HttpClient-backed
 * service would return. Swap the `of(...)` bodies for real `http.get<T>(...)`
 * calls when a backend is available — the public API surface stays the same.
 */
function respond<T>(value: T): Observable<T> {
  return of(value).pipe(delay(150));
}

@Injectable({ providedIn: 'root' })
export class StudentService {
  getAll(): Observable<Models.Student[]> { return respond(M.STUDENTS); }
  getById(id: string): Observable<Models.Student | undefined> { return respond(M.STUDENTS.find(s => s.id === id)); }
  getStats() {
    const total = M.STUDENTS.length;
    const boys = M.STUDENTS.filter(s => s.gender === 'Male').length;
    const girls = M.STUDENTS.filter(s => s.gender === 'Female').length;
    const active = M.STUDENTS.filter(s => s.status === 'Active').length;
    return respond({ total, boys, girls, active, inactive: total - active });
  }
}

@Injectable({ providedIn: 'root' })
export class TeacherService {
  getAll(): Observable<Models.Teacher[]> { return respond(M.TEACHERS); }
  getById(id: string): Observable<Models.Teacher | undefined> { return respond(M.TEACHERS.find(t => t.id === id)); }
}

@Injectable({ providedIn: 'root' })
export class ClassService {
  getAll(): Observable<Models.SchoolClass[]> { return respond(M.CLASSES); }
  getSections(): Observable<Models.Section[]> { return respond(M.ALL_SECTIONS); }
}

@Injectable({ providedIn: 'root' })
export class SubjectService {
  getAll(): Observable<Models.Subject[]> { return respond(M.SUBJECTS); }
}

@Injectable({ providedIn: 'root' })
export class SessionService {
  getAll(): Observable<Models.AcademicSession[]> { return respond(M.SESSIONS); }
  getActiveLabel(): string { return M.currentSessionLabel(); }
}

@Injectable({ providedIn: 'root' })
export class AttendanceService {
  getForDate(date: string, classId?: string, sectionId?: string): Observable<Models.AttendanceRecord[]> {
    return respond(M.generateAttendanceForDate(date, classId, sectionId));
  }
  getTeacherAttendanceToday(): Observable<Models.TeacherAttendanceRecord[]> { return respond(M.TEACHER_ATTENDANCE_TODAY); }
}

@Injectable({ providedIn: 'root' })
export class TimetableService {
  getTimetable(classId: string, sectionId: string): Observable<Models.ClassTimetable> {
    return respond(M.generateTimetable(classId, sectionId));
  }
}

@Injectable({ providedIn: 'root' })
export class HomeworkService {
  getAll(): Observable<Models.Homework[]> { return respond(M.HOMEWORK); }
}

@Injectable({ providedIn: 'root' })
export class ExamService {
  getAll(): Observable<Models.Exam[]> { return respond(M.EXAMS); }
  getMarks(classId: string, subjects: string[]): Observable<Models.MarksEntry[]> {
    return respond(M.generateMarks(classId, subjects));
  }
}

@Injectable({ providedIn: 'root' })
export class AdmissionService {
  getAll(): Observable<Models.Admission[]> { return respond(M.ADMISSIONS); }
}

@Injectable({ providedIn: 'root' })
export class FeeService {
  getStructure() { return respond(M.FEE_STRUCTURE); }
  getStudentFees(): Observable<Models.StudentFee[]> { return respond(M.STUDENT_FEES); }
  getPayments(): Observable<Models.Payment[]> { return respond(M.PAYMENTS); }
}

@Injectable({ providedIn: 'root' })
export class LibraryService {
  getBooks(): Observable<Models.Book[]> { return respond(M.BOOKS); }
  getIssues(): Observable<Models.LibraryIssue[]> { return respond(M.LIBRARY_ISSUES); }
}

@Injectable({ providedIn: 'root' })
export class TransportService {
  getVehicles(): Observable<Models.Vehicle[]> { return respond(M.VEHICLES); }
  getRoutes(): Observable<Models.TransportRoute[]> { return respond(M.TRANSPORT_ROUTES); }
}

@Injectable({ providedIn: 'root' })
export class HostelService {
  getRooms(): Observable<Models.HostelRoom[]> { return respond(M.HOSTEL_ROOMS); }
}

@Injectable({ providedIn: 'root' })
export class EventService {
  getAll(): Observable<Models.SchoolEvent[]> { return respond(M.SCHOOL_EVENTS); }
}

@Injectable({ providedIn: 'root' })
export class NoticeService {
  getAll(): Observable<Models.Notice[]> { return respond(M.NOTICES); }
}

@Injectable({ providedIn: 'root' })
export class NotificationService {
  getAll(): Observable<Models.AppNotification[]> { return respond(M.NOTIFICATIONS); }
}

@Injectable({ providedIn: 'root' })
export class ActivityService {
  getAll(): Observable<Models.Activity[]> { return respond(M.ACTIVITIES); }
}
