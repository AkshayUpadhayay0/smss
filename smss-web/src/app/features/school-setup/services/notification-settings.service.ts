import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { FeeReminderPreview, NotificationSettings, NotificationSettingsRequest } from '../models/notification-settings.model';

const BASE = '/api/NotificationSettings';

/** A school's push-notification settings. The server takes the school from the token, so no school id is ever sent. Every call is School Admin only (403 otherwise). */
@Injectable({ providedIn: 'root' })
export class NotificationSettingsService {
  private readonly api = inject(ApiService);

  /** The screen shows its own message when loading fails (including a 403), so no global toast. */
  get(): Observable<NotificationSettings> {
    return this.api.get<NotificationSettings>(BASE, undefined, { skipErrorToast: true });
  }

  /** The caller toasts the server's reason (a 400 explains what is wrong, a 403 says who may save). */
  save(request: NotificationSettingsRequest): Observable<NotificationSettings> {
    return this.api.put<NotificationSettings>(BASE, request, { skipErrorToast: true });
  }

  /** Dry run for today, based on the SAVED settings. Sends nothing and writes nothing. */
  previewFeeReminders(): Observable<FeeReminderPreview> {
    return this.api.get<FeeReminderPreview>(`${BASE}/fee-reminders/preview`, undefined, { skipErrorToast: true });
  }
}
