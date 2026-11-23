import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';

const BASE = '/api/ClassSubjects';

/** One subject mapped to one class (names are joined by the server). */
export interface ClassSubject {
  classSubjectId: number;
  classId: number;
  className: string;
  subjectId: number;
  subjectName: string;
  subjectCode?: string | null;
}

/** The school is resolved from the token by the server; only class and subject ids are ever sent. */
@Injectable({ providedIn: 'root' })
export class ClassSubjectService {
  private readonly api = inject(ApiService);

  /** The class's subjects in one academic year (omitted = the school's current year). */
  listByClass(classId: number, academicYearId?: number): Observable<ClassSubject[]> {
    return this.api.get<ClassSubject[]>(BASE, academicYearId != null ? { classId, academicYearId } : { classId }).pipe(map((rows) => rows ?? []));
  }

  /** Replaces the class's whole subject set: adds the missing pairs, removes the ones no longer listed. */
  setSubjects(classId: number, subjectIds: number[], academicYearId?: number): Observable<ClassSubject[]> {
    return this.api.post<ClassSubject[]>(`${BASE}/bulk`, academicYearId != null ? { classId, subjectIds, academicYearId } : { classId, subjectIds }).pipe(map((rows) => rows ?? []));
  }
}
