import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { PendingStudentDocuments, StudentDocument, StudentDocuments } from '../models/student-document.model';

const BASE = '/api/StudentDocuments';

/** A student's documents. The school always comes from the signed-in session on the server; nothing here names it. Files are fetched only through the authenticated download. */
@Injectable({ providedIn: 'root' })
export class StudentDocumentService {
  private readonly api = inject(ApiService);

  /** The statuses and documents of one student. {@code quiet} keeps a refusal (a teacher who is not this section's class teacher) from toasting: the card simply hides. */
  get(studentId: string, quiet = false): Observable<StudentDocuments> {
    return this.api.get<StudentDocuments>(`${BASE}/${encodeURIComponent(studentId)}`, undefined, quiet ? { skipErrorToast: true } : undefined);
  }

  upload(studentId: string, file: File, documentTypeId: number, remarks: string | null): Observable<StudentDocument> {
    const extra: Record<string, string> = { documentTypeId: String(documentTypeId) };
    if (remarks) extra['remarks'] = remarks;
    return this.api.upload<StudentDocument>(`${BASE}/${encodeURIComponent(studentId)}/upload`, file, 'file', undefined, extra);
  }

  waive(studentId: string, documentTypeId: number, reason: string): Observable<StudentDocument> {
    return this.api.post<StudentDocument>(`${BASE}/${encodeURIComponent(studentId)}/waive`, { documentTypeId, reason });
  }

  /** Voids (hides) a document or a waiver: the record and the file stay for the audit trail. */
  voidDocument(studentId: string, documentId: number, reason: string | null): Observable<StudentDocument> {
    return this.api.post<StudentDocument>(`${BASE}/${encodeURIComponent(studentId)}/${documentId}/void`, { reason });
  }

  download(studentId: string, documentId: number): Observable<Blob> {
    return this.api.download(`${BASE}/${encodeURIComponent(studentId)}/${documentId}/download`);
  }

  pending(filters: { classId?: number; sectionId?: number; search?: string } = {}): Observable<PendingStudentDocuments> {
    const params: Record<string, string | number> = {};
    if (filters.classId) params['classId'] = filters.classId;
    if (filters.sectionId) params['sectionId'] = filters.sectionId;
    if (filters.search?.trim()) params['search'] = filters.search.trim();
    return this.api.get<PendingStudentDocuments>(`${BASE}/pending`, params);
  }

  pendingCount(): Observable<number> {
    return this.api.get<number>(`${BASE}/pending/count`);
  }
}
