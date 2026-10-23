import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { ClassTeacherAssignment, ClassTeacherList, EligibleTeacher } from '../models/class-teacher.model';

const BASE = '/api/ClassTeacher';

/** Always "my school's" class teachers: the server resolves the school from the token, so no school id is ever sent. */
@Injectable({ providedIn: 'root' })
export class ClassTeacherService {
  private readonly api = inject(ApiService);

  /** Every active section with its class teacher (or none) for one academic year. */
  list(academicYearId: number): Observable<ClassTeacherList> {
    return this.api.get<ClassTeacherList>(BASE, { academicYearId });
  }

  eligibleTeachers(): Observable<EligibleTeacher[]> {
    return this.api.get<EligibleTeacher[]>(`${BASE}/eligible-teachers`).pipe(map((l) => l ?? []));
  }

  /** Assigns or reassigns in one server transaction (the previous teacher's row is kept as history). */
  assign(academicYearId: number, classId: number, sectionId: number, employeeId: string): Observable<ClassTeacherAssignment> {
    return this.api.post<ClassTeacherAssignment>(`${BASE}/assign`, { academicYearId, classId, sectionId, employeeId });
  }

  unassign(academicYearId: number, sectionId: number): Observable<ClassTeacherAssignment> {
    return this.api.post<ClassTeacherAssignment>(`${BASE}/unassign`, { academicYearId, sectionId });
  }
}
