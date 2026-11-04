import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { IdCardPreview, IdCardPreviewRequest, IdCardSettings, IdCardSettingsRequest, IdCardSignatureState } from '../models/id-card-settings.model';

const BASE = '/api/IdCardSettings';

/** A school's ID card template and branding. The server takes the school from the token, so no school id is ever sent. */
@Injectable({ providedIn: 'root' })
export class IdCardSettingsService {
  private readonly api = inject(ApiService);

  get(): Observable<IdCardSettings> {
    return this.api.get<IdCardSettings>(BASE);
  }

  save(request: IdCardSettingsRequest): Observable<IdCardSettings> {
    return this.api.put<IdCardSettings>(BASE, request);
  }

  /** The principal signature goes to PRIVATE storage on the server; the caller handles failures itself (a toast). */
  uploadSignature(file: File): Observable<IdCardSignatureState> {
    return this.api.upload<IdCardSignatureState>(`${BASE}/signature`, file, 'file', { skipErrorToast: true });
  }

  removeSignature(): Observable<IdCardSignatureState> {
    return this.api.post<IdCardSignatureState>(`${BASE}/signature/remove`, {}, { skipErrorToast: true });
  }

  /** Both faces of a template as PNG with the given (possibly unsaved) settings. Previews are best-effort: the screen shows its own placeholder on failure. */
  preview(request: IdCardPreviewRequest): Observable<IdCardPreview> {
    return this.api.post<IdCardPreview>(`${BASE}/preview`, request, { skipErrorToast: true });
  }
}
