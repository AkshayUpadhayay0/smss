import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { ChangeSectionRequest, Leaver, LeavingCheck, LeavingCorrectionCheck, RecordLeavingRequest, RecordLeavingResult, SectionChangeCheck, SectionChangeHistory, SectionChangeResult } from '../models/student-leaving.model';

/** School Admin and Principal only. The school comes from the token; no school id is ever sent. */
@Injectable({ providedIn: 'root' })
export class StudentLeavingService {
  private readonly api = inject(ApiService);

  check(studentId: string, leavingDate?: string): Observable<LeavingCheck> { return this.api.get<LeavingCheck>('/api/StudentLeaving/check', leavingDate ? { studentId, leavingDate } : { studentId }); }
  record(request: RecordLeavingRequest): Observable<RecordLeavingResult> { return this.api.post<RecordLeavingResult>('/api/StudentLeaving/record', request); }
  checkCorrection(studentId: string): Observable<LeavingCorrectionCheck> { return this.api.get<LeavingCorrectionCheck>('/api/StudentLeaving/correction/check', { studentId }); }
  correct(studentId: string, reason: string): Observable<LeavingCorrectionCheck> { return this.api.post<LeavingCorrectionCheck>('/api/StudentLeaving/correction', { studentId, reason, confirm: true }); }
  leavers(): Observable<Leaver[]> { return this.api.get<Leaver[]>('/api/StudentLeaving/leavers'); }

  sectionCheck(studentId: string): Observable<SectionChangeCheck> { return this.api.get<SectionChangeCheck>('/api/SectionChanges/check', { studentId }); }
  changeSection(request: ChangeSectionRequest): Observable<SectionChangeResult> { return this.api.post<SectionChangeResult>('/api/SectionChanges', request); }
  /** The history is read quietly: a failure must not toast on the student's profile. */
  sectionHistory(studentId: string): Observable<SectionChangeHistory[]> { return this.api.get<SectionChangeHistory[]>('/api/SectionChanges/history', { studentId }, { skipErrorToast: true }); }
}
