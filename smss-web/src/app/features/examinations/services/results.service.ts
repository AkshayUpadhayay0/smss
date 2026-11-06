import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { MyReportCard, MyReportCardDetail, Publication, PublicationDetail, PublishRequest, PublishResult, ResultPreview, ResultScope } from '../models/results.model';

/** Results and publishing, and the student's own published cards. The school (and, for a student, the student) always comes from the token. */
@Injectable({ providedIn: 'root' })
export class ResultsService {
  private readonly api = inject(ApiService);

  preview(schemeId: number, sectionId: number, scope: ResultScope, termId?: number | null): Observable<ResultPreview> {
    return this.api.get<ResultPreview>('/api/ReportResult/preview', { schemeId, sectionId, scope, ...(termId ? { termId } : {}) });
  }
  /** The 409 that asks for confirmation (incomplete students, unmarked attendance days) is handled by the screen, so its error toast is skipped. */
  publish(req: PublishRequest): Observable<PublishResult> { return this.api.post<PublishResult>('/api/ReportResult/publish', req, { skipErrorToast: true }); }
  publications(schemeId: number, sectionId: number): Observable<Publication[]> { return this.api.get<Publication[]>('/api/ReportResult/publications', { schemeId, sectionId }); }
  publication(publicationId: number): Observable<PublicationDetail> { return this.api.get<PublicationDetail>(`/api/ReportResult/publications/${publicationId}`); }
  cardPdf(publicationId: number, studentId: string): Observable<Blob> { return this.api.download(`/api/ReportResult/publications/${publicationId}/cards/${encodeURIComponent(studentId)}/pdf`); }
  draftPdf(schemeId: number, sectionId: number, scope: ResultScope, termId: number | null | undefined, studentId: string): Observable<Blob> {
    return this.api.download(`/api/ReportResult/draft-pdf?schemeId=${schemeId}&sectionId=${sectionId}&scope=${scope}${termId ? `&termId=${termId}` : ''}&studentId=${encodeURIComponent(studentId)}`);
  }

  myCards(): Observable<MyReportCard[]> { return this.api.get<MyReportCard[]>('/api/Student/me/report-cards'); }
  myCard(publicationId: number): Observable<MyReportCardDetail> { return this.api.get<MyReportCardDetail>(`/api/Student/me/report-cards/${publicationId}`); }
  myCardPdf(publicationId: number): Observable<Blob> { return this.api.download(`/api/Student/me/report-cards/${publicationId}/pdf`); }
}
