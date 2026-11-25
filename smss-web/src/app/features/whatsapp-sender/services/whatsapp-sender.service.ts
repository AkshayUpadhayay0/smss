import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { SaveSenderRequest, SenderStatusModel } from '../models/whatsapp-sender.model';

const BASE = '/api/WhatsAppSender';

/** A school's WhatsApp sender status. Nothing here sends a message or carries a credential. */
@Injectable({ providedIn: 'root' })
export class WhatsAppSenderService {
  private readonly api = inject(ApiService);

  /** The signed-in School Admin's or Principal's OWN school (the server takes the school from the token). {@code quiet} keeps a refusal from toasting. */
  mine(quiet = true): Observable<SenderStatusModel> {
    return this.api.get<SenderStatusModel>(`${BASE}/me`, undefined, quiet ? { skipErrorToast: true } : undefined);
  }

  /** Any school: Super Admin. */
  get(schoolId: string): Observable<SenderStatusModel> {
    return this.api.get<SenderStatusModel>(`${BASE}/schools/${encodeURIComponent(schoolId)}`);
  }

  save(schoolId: string, request: SaveSenderRequest): Observable<SenderStatusModel> {
    return this.api.put<SenderStatusModel>(`${BASE}/schools/${encodeURIComponent(schoolId)}`, request);
  }
}
