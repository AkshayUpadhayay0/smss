import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, from, switchMap, throwError } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { BulkPart, BulkTooLarge } from '../models/report-card-settings.model';
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
  /**
   * Every card of a published version as one PDF in roll order (optionally one roll range of it). Over the server's cap the answer is a 409 that
   * lists the roll ranges to download instead: that arrives here as a BulkDownloadError, never as a silent truncation.
   */
  bulkPdf(publicationId: number, range?: { fromRoll: string; toRoll: string } | null): Observable<Blob> {
    return this.api.postForBlob(`/api/ReportCard/${publicationId}/bulk`, range ?? {}, { skipErrorToast: true }).pipe(catchError((e: unknown) => from(readBulkError(e)).pipe(switchMap((err) => throwError(() => err)))));
  }
  draftPdf(schemeId: number, sectionId: number, scope: ResultScope, termId: number | null | undefined, studentId: string): Observable<Blob> {
    return this.api.download(`/api/ReportResult/draft-pdf?schemeId=${schemeId}&sectionId=${sectionId}&scope=${scope}${termId ? `&termId=${termId}` : ''}&studentId=${encodeURIComponent(studentId)}`);
  }

  myCards(): Observable<MyReportCard[]> { return this.api.get<MyReportCard[]>('/api/Student/me/report-cards'); }
  myCard(publicationId: number): Observable<MyReportCardDetail> { return this.api.get<MyReportCardDetail>(`/api/Student/me/report-cards/${publicationId}`); }
  myCardPdf(publicationId: number): Observable<Blob> { return this.api.download(`/api/Student/me/report-cards/${publicationId}/pdf`); }
}

/** A bulk download that could not be made: the server's plain message and, when the version is over the cap, the parts to download instead. */
export class BulkDownloadError extends Error {
  constructor(message: string, readonly parts: BulkPart[] = [], readonly maxPerRequest = 0) { super(message); }
}

async function readBulkError(e: unknown): Promise<BulkDownloadError> {
  const generic = 'The report cards could not be prepared. Please try again.';
  if (!(e instanceof HttpErrorResponse)) return new BulkDownloadError(generic);
  try {
    const body = (e.error instanceof Blob ? JSON.parse(await e.error.text()) : e.error) as { message?: unknown; data?: BulkTooLarge | null } | null;
    const message = typeof body?.message === 'string' && body.message ? body.message : generic;
    return new BulkDownloadError(message, body?.data?.parts ?? [], body?.data?.maxPerRequest ?? 0);
  } catch { return new BulkDownloadError(generic); }
}
