import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { AssignClassTeacherRequest, ClassTeacherAssignment, ClassTeacherList, EligibleTeacher } from '../models/academic-setup.model';

const BASE = '/api/ClassTeacher';

/** Always "my school's" class teachers: the server resolves the school from the token. Nothing is ever deleted: a change closes the old row and opens a new one. */
@Injectable({ providedIn: 'root' })
export class ClassTeacherApiService {
  private readonly api = inject(ApiService);

  list(academicYearId: number): Observable<ClassTeacherList> {
    return this.api.get<ClassTeacherList>(BASE, { academicYearId });
  }

  eligible(): Observable<EligibleTeacher[]> {
    return this.api.get<EligibleTeacher[]>(`${BASE}/eligible-teachers`).pipe(map((rows) => rows ?? []));
  }

  /** Assigns or reassigns (the previous teacher's row is closed in the same transaction). */
  assign(request: AssignClassTeacherRequest): Observable<ClassTeacherAssignment> {
    return this.api.post<ClassTeacherAssignment>(`${BASE}/assign`, request);
  }

  unassign(academicYearId: number, sectionId: number, reason: string | null): Observable<ClassTeacherAssignment> {
    return this.api.post<ClassTeacherAssignment>(`${BASE}/unassign`, { academicYearId, sectionId, reason });
  }

  history(academicYearId: number, sectionId: number): Observable<ClassTeacherAssignment[]> {
    return this.api.get<ClassTeacherAssignment[]>(`${BASE}/history`, { academicYearId, sectionId }).pipe(map((rows) => rows ?? []));
  }
}
