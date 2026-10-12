import { Injectable, inject } from '@angular/core';
import { Observable, map, throwError } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { AuthService } from '../../../core/services/auth.service';
import { StudentListItem, StudentPhoto, StudentProfile } from '../models/student.model';

const BASE = '/api/Student';

/**
 * The routes carry the school id. It always comes from the signed-in session, and the server refuses any id
 * that isn't the caller's own school.
 */
@Injectable({ providedIn: 'root' })
export class StudentService {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);

  /** Students enrolled in a section (a tb_sections id) for an academic year. */
  list(academicYearId: number, classSectionId: number): Observable<StudentListItem[]> {
    return this.withSchool((s) => this.api.get<StudentListItem[]>(`${BASE}/${s}`, { academicYearId, classSectionId })).pipe(map((l) => l ?? []));
  }

  getProfile(studentId: number): Observable<StudentProfile> {
    return this.withSchool((s) => this.api.get<StudentProfile>(`${BASE}/${s}/${studentId}`));
  }

  /**
   * Same two-step pattern as the school logo: the student is created first, then the photo is sent on its own.
   * Callers handle a failure themselves (the student is already saved), so no global error toast.
   */
  uploadPhoto(studentId: number, file: File): Observable<StudentPhoto> {
    return this.withSchool((s) => this.api.upload<StudentPhoto>(`${BASE}/${s}/${studentId}/photo`, file, 'file', { skipErrorToast: true }));
  }

  removePhoto(studentId: number): Observable<StudentPhoto> {
    return this.withSchool((s) => this.api.post<StudentPhoto>(`${BASE}/${s}/${studentId}/photo/remove`, {}, { skipErrorToast: true }));
  }

  private withSchool<T>(call: (schoolId: string) => Observable<T>): Observable<T> {
    const schoolId = this.auth.user()?.schoolId;
    return schoolId ? call(encodeURIComponent(schoolId)) : throwError(() => new Error('Your account is not linked to a school.'));
  }
}
