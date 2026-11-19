/** Mirrors NotificationSettingsDto. With no saved row the server returns the defaults (reminders off, 09:00, 0 grace days). */
export interface NotificationSettings {
  feeReminderEnabled: boolean;
  /** HH:mm, India time. */
  feeReminderSendTime: string;
  feeReminderGraceDays: number;
  /** yyyy-MM-dd of the last daily run, or null. */
  lastFeeReminderRunDate: string | null;
}

/** Mirrors NotificationSettingsRequest. */
export interface NotificationSettingsRequest {
  feeReminderEnabled: boolean;
  feeReminderSendTime: string;
  feeReminderGraceDays: number;
}

/** Mirrors FeeReminderPreviewDto: the dry run of today's reminders (it sends and writes nothing). */
export interface FeeReminderPreview {
  date: string;
  graceDays: number;
  studentsToRemind: number;
  withDevice: number;
  withoutDevice: number;
}

/** The server's own limits (NotificationRules): the send time is 24-hour HH:mm, the grace period 0..30 whole days. */
export const MAX_GRACE_DAYS = 30;
export const SEND_TIME = /^([01]\d|2[0-3]):[0-5]\d$/;
