import { Injectable, inject } from '@angular/core';
import { Observable, map, throwError } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { AuthService } from '../../../core/services/auth.service';
import { StudentFees } from '../models/student-fees.model';
import { StudentListItem, StudentLoginCreated, StudentPhoto, StudentProfile } from '../models/student.model';

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

  getProfile(studentId: string): Observable<StudentProfile> {
    return this.withSchool((s) => this.api.get<StudentProfile>(`${BASE}/${s}/${encodeURIComponent(studentId)}`));
  }

  /**
   * Same two-step pattern as the school logo: the student is created first, then the photo is sent on its own.
   * Callers handle a failure themselves (the student is already saved), so no global error toast.
   */
  uploadPhoto(studentId: string, file: File): Observable<StudentPhoto> {
    return this.withSchool((s) => this.api.upload<StudentPhoto>(`${BASE}/${s}/${encodeURIComponent(studentId)}/photo`, file, 'file', { skipErrorToast: true }));
  }

  removePhoto(studentId: string): Observable<StudentPhoto> {
    return this.withSchool((s) => this.api.post<StudentPhoto>(`${BASE}/${s}/${encodeURIComponent(studentId)}/photo/remove`, {}, { skipErrorToast: true }));
  }

  /** Creates the student's login; the response carries the generated Login ID and temporary password (shown once). */
  createLogin(studentId: string): Observable<StudentLoginCreated> {
    return this.withSchool((s) => this.api.post<StudentLoginCreated>(`${BASE}/${s}/${encodeURIComponent(studentId)}/create-login`, {}));
  }

  // ---- fees (the server takes the school from the token; no school id in these routes)
  getFees(studentId: string): Observable<StudentFees> {
    return this.api.get<StudentFees>(`/api/StudentFee/${encodeURIComponent(studentId)}`);
  }

  assignFeeStructure(studentId: string, feeStructureId: number): Observable<StudentFees> {
    return this.api.put<StudentFees>(`/api/StudentFee/${encodeURIComponent(studentId)}/structure`, { feeStructureId });
  }

  applyConcession(studentId: string, concessionId: number, remarks: string | null): Observable<StudentFees> {
    return this.api.post<StudentFees>(`/api/StudentFee/${encodeURIComponent(studentId)}/concessions`, { concessionId, remarks });
  }

  removeConcession(studentId: string, concessionId: number): Observable<StudentFees> {
    return this.api.delete<StudentFees>(`/api/StudentFee/${encodeURIComponent(studentId)}/concessions/${concessionId}`);
  }

  private withSchool<T>(call: (schoolId: string) => Observable<T>): Observable<T> {
    const schoolId = this.auth.user()?.schoolId;
    return schoolId ? call(encodeURIComponent(schoolId)) : throwError(() => new Error('Your account is not linked to a school.'));
  }
}
