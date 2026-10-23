import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import {
  Assessment,
  AssessmentDetail,
  AssessmentFilters,
  AssessmentType,
  CreateAssessmentRequest,
  RosterStudent,
  StudentResult,
  UpdateAssessmentRequest,
} from '../models/assessment.model';

const BASE = '/api/Assessment';

/** The school and the acting user come from the token: nothing about them is ever sent. */
@Injectable({ providedIn: 'root' })
export class AssessmentService {
  private readonly api = inject(ApiService);

  types(): Observable<AssessmentType[]> {
    return this.api.get<AssessmentType[]>(`${BASE}/types`).pipe(map((l) => l ?? []));
  }

  /** The students a test for that section + subject has to cover (same eligibility rules as creating the test). */
  roster(academicYearId: number, sectionId: number, subjectId: number): Observable<RosterStudent[]> {
    return this.api.get<RosterStudent[]>(`${BASE}/roster`, { academicYearId, sectionId, subjectId }).pipe(map((l) => l ?? []));
  }

  /** A School Admin: the whole school, filterable. */
  list(filters: AssessmentFilters): Observable<Assessment[]> {
    return this.api.get<Assessment[]>(BASE, this.params(filters)).pipe(map((l) => l ?? []));
  }

  /** A Teacher: the tests they recorded. */
  my(filters: AssessmentFilters): Observable<Assessment[]> {
    return this.api.get<Assessment[]>(`${BASE}/my`, this.params(filters)).pipe(map((l) => l ?? []));
  }

  get(id: number): Observable<AssessmentDetail> {
    return this.api.get<AssessmentDetail>(`${BASE}/${id}`);
  }

  create(request: CreateAssessmentRequest): Observable<AssessmentDetail> {
    return this.api.post<AssessmentDetail>(BASE, request);
  }

  update(id: number, request: UpdateAssessmentRequest): Observable<AssessmentDetail> {
    return this.api.put<AssessmentDetail>(`${BASE}/${id}`, request);
  }

  remove(id: number): Observable<Assessment> {
    return this.api.delete<Assessment>(`${BASE}/${id}`);
  }

  /** A Student: their own results, most recent first. */
  studentMy(filters: Pick<AssessmentFilters, 'from' | 'to'> = {}): Observable<StudentResult[]> {
    return this.api.get<StudentResult[]>(`${BASE}/student/my`, this.params(filters)).pipe(map((l) => l ?? []));
  }

  private params(filters: AssessmentFilters): Record<string, string | number> {
    const params: Record<string, string | number> = {};
    for (const [key, value] of Object.entries(filters)) if (value !== undefined && value !== null && value !== '') params[key] = value as string | number;
    return params;
  }
}
