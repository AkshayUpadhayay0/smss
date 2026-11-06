import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, from, switchMap, throwError } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import {
  ActivityGrades, Completeness, GridEntryRequest, LockResult, MarksContext, MarksGrid, TermAttendance, TermStudent, TermStudents, UploadPreview, YearStudent, YearStudents,
  ActivityStudent,
} from '../models/marks.model';

/** Marks entry and the class report. The school always comes from the token. */
@Injectable({ providedIn: 'root' })
export class MarksService {
  private readonly api = inject(ApiService);

  context(academicYearId?: number): Observable<MarksContext> {
    return this.api.get<MarksContext>('/api/ReportMarks/context', academicYearId ? { academicYearId } : undefined);
  }
  grid(examSubjectId: number, sectionId: number): Observable<MarksGrid> { return this.api.get<MarksGrid>('/api/ReportMarks/grid', { examSubjectId, sectionId }); }
  save(examSubjectId: number, sectionId: number, version: number, entries: GridEntryRequest[]): Observable<MarksGrid> {
    return this.api.put<MarksGrid>('/api/ReportMarks/grid', { examSubjectId, sectionId, version, entries });
  }
  submit(examSubjectId: number, sectionId: number, version: number): Observable<MarksGrid> { return this.api.post<MarksGrid>('/api/ReportMarks/submit', { examSubjectId, sectionId, version }); }
  accept(examSubjectId: number, sectionId: number, version: number): Observable<MarksGrid> { return this.api.post<MarksGrid>('/api/ReportMarks/accept', { examSubjectId, sectionId, version }); }
  reopen(examSubjectId: number, sectionId: number, version: number, reason: string): Observable<MarksGrid> {
    return this.api.post<MarksGrid>('/api/ReportMarks/reopen', { examSubjectId, sectionId, version, reason });
  }
  template(examSubjectId: number, sectionId: number): Observable<Blob> {
    return this.api.download(`/api/ReportMarks/template?examSubjectId=${examSubjectId}&sectionId=${sectionId}`, { skipErrorToast: true }).pipe(catchError(rethrowBlobError));
  }
  /** A dry run: validates the sheet and answers with what it would put in the grid. Nothing is saved. */
  uploadPreview(examSubjectId: number, sectionId: number, file: File): Observable<UploadPreview> {
    return this.api.upload<UploadPreview>(`/api/ReportMarks/upload?examSubjectId=${examSubjectId}&sectionId=${sectionId}&apply=false`, file);
  }

  completeness(schemeId: number, sectionId: number): Observable<Completeness> { return this.api.get<Completeness>('/api/ReportClass/completeness', { schemeId, sectionId }); }
  activity(termId: number, sectionId: number): Observable<ActivityGrades> { return this.api.get<ActivityGrades>('/api/ReportClass/activity-grades', { termId, sectionId }); }
  saveActivity(termId: number, sectionId: number, students: ActivityStudent[], areaIds: number[]): Observable<ActivityGrades> {
    const grades = students.flatMap((s) => areaIds.map((a) => ({ studentId: s.studentId, areaId: a, gradeCode: s.grades[String(a)] ?? '' })));
    return this.api.put<ActivityGrades>('/api/ReportClass/activity-grades', { termId, sectionId, grades });
  }
  termStudents(termId: number, sectionId: number): Observable<TermStudents> { return this.api.get<TermStudents>('/api/ReportClass/term-students', { termId, sectionId }); }
  saveTermStudents(termId: number, sectionId: number, students: TermStudent[]): Observable<TermStudents> {
    return this.api.put<TermStudents>('/api/ReportClass/term-students', { termId, sectionId, students: students.map((s) => ({ studentId: s.studentId, remark: s.remark ?? null, conduct: s.conduct ?? null, rankText: s.rankText ?? null, manualWorkingDays: s.manualWorkingDays ?? null, manualPresentDays: s.manualPresentDays ?? null })) });
  }
  yearStudents(schemeId: number, sectionId: number): Observable<YearStudents> { return this.api.get<YearStudents>('/api/ReportClass/year-students', { schemeId, sectionId }); }
  saveYearStudents(schemeId: number, sectionId: number, students: YearStudent[]): Observable<YearStudents> {
    return this.api.put<YearStudents>('/api/ReportClass/year-students', { schemeId, sectionId, students: students.map((s) => ({ studentId: s.studentId, promotionDecision: s.promotionDecision ?? null, promotedTo: s.promotedTo ?? null, newSessionDate: s.newSessionDate || null })) });
  }
  attendance(termId: number, sectionId: number): Observable<TermAttendance> { return this.api.get<TermAttendance>('/api/ReportClass/attendance', { termId, sectionId }); }
  /** The 409 that asks for confirmation (students added after the marks were accepted) is handled by the screen, so its error toast is skipped. */
  lock(termId: number, sectionId: number, confirmExcluded = false): Observable<LockResult> { return this.api.post<LockResult>('/api/ReportClass/lock', { termId, sectionId, confirmExcluded }, { skipErrorToast: true }); }
  unlock(termId: number, sectionId: number, reason: string): Observable<LockResult> { return this.api.post<LockResult>('/api/ReportClass/unlock', { termId, sectionId, reason }); }
}

function rethrowBlobError(e: unknown): Observable<never> { return from(readBlobError(e)).pipe(switchMap((message) => throwError(() => new Error(message)))); }
async function readBlobError(e: unknown): Promise<string> {
  const generic = 'The file could not be prepared. Please try again.';
  if (!(e instanceof HttpErrorResponse)) return generic;
  try {
    const body: unknown = e.error instanceof Blob ? JSON.parse(await e.error.text()) : e.error;
    const message = (body as { message?: unknown } | null)?.message;
    return typeof message === 'string' && message ? message : generic;
  } catch { return generic; }
}
