import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { ReportCardPreview, ReportCardPreviewRequest, ReportCardSchemeChoice, ReportCardSettings, ReportCardSettingsRequest, ReportCardSignatureState } from '../models/report-card-settings.model';

const BASE = '/api/ReportCardSettings';

/** A school's report card branding and design choice. The school always comes from the token. School Admin writes; Principal reads. */
@Injectable({ providedIn: 'root' })
export class ReportCardSettingsService {
  private readonly api = inject(ApiService);

  get(): Observable<ReportCardSettings> { return this.api.get<ReportCardSettings>(BASE); }
  save(request: ReportCardSettingsRequest): Observable<ReportCardSettings> { return this.api.put<ReportCardSettings>(BASE, request); }
  uploadSignature(file: File): Observable<ReportCardSignatureState> { return this.api.upload<ReportCardSignatureState>(`${BASE}/signature`, file, 'file', { skipErrorToast: true }); }
  removeSignature(): Observable<ReportCardSignatureState> { return this.api.post<ReportCardSignatureState>(`${BASE}/signature/remove`, {}, { skipErrorToast: true }); }
  /** Which design a report scheme prints with (only built designs that fit the scheme's mode are accepted by the server). */
  setSchemeDesign(schemeId: number, templateKey: string): Observable<ReportCardSchemeChoice | unknown> { return this.api.put(`${BASE}/scheme-design`, { schemeId, templateKey }); }
  /** A design drawn with the on-screen (possibly unsaved) settings on sample data. Best-effort: the screen shows its own placeholder on failure. */
  preview(request: ReportCardPreviewRequest): Observable<ReportCardPreview> { return this.api.post<ReportCardPreview>(`${BASE}/preview`, request, { skipErrorToast: true }); }
}
