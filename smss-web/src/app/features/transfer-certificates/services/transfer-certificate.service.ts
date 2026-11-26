import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { IssueTcRequest, TcCheck, TransferCertificate } from '../models/transfer-certificate.model';

/** School Admin and Principal only. The school comes from the token; no school id is ever sent. The PDF comes from an authenticated endpoint as raw bytes. */
@Injectable({ providedIn: 'root' })
export class TransferCertificateService {
  private readonly api = inject(ApiService);

  check(studentId: string): Observable<TcCheck> { return this.api.get<TcCheck>('/api/TransferCertificates/check', { studentId }); }
  issue(request: IssueTcRequest): Observable<TransferCertificate> { return this.api.post<TransferCertificate>('/api/TransferCertificates/issue', request); }
  list(status?: string): Observable<TransferCertificate[]> { return this.api.get<TransferCertificate[]>('/api/TransferCertificates', status ? { status } : undefined); }
  /** The original, drawn again from the stored snapshot (the server records the print). */
  original(id: number): Observable<Blob> { return this.api.download(`/api/TransferCertificates/${id}/pdf`); }
  /** A duplicate: same number, marked "DUPLICATE COPY No. N" (the server records the print). */
  duplicate(id: number): Observable<Blob> { return this.api.postForBlob(`/api/TransferCertificates/${id}/duplicate`); }
  void(id: number, reason: string): Observable<TransferCertificate> { return this.api.post<TransferCertificate>(`/api/TransferCertificates/${id}/void`, { reason, confirm: true }); }
}
