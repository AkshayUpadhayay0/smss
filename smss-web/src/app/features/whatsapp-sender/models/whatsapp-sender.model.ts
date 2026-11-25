// Mirrors SenderStatusDto / SaveSenderRequestDto. There is NO credential anywhere in these: tokens never reach the browser or the database.

export type SenderStatus = 'NOT_REGISTERED' | 'PENDING' | 'ACTIVE';

export const SENDER_STATUSES: readonly { value: SenderStatus; label: string }[] = [
  { value: 'NOT_REGISTERED', label: 'Not registered' },
  { value: 'PENDING', label: 'Pending (registration with Meta in progress)' },
  { value: 'ACTIVE', label: 'Active (the school\'s own number sends)' },
];

export interface SenderStatusModel {
  schoolId: string;
  schoolName: string;
  whatsAppNumber: string | null;
  status: SenderStatus;
  statusLabel: string;
  /** In words: whose number the school's messages go from. */
  sendsFrom: string;
  verifiedAt: string | null;
  updatedAt: string;
  /** The Meta phone-number id (an identifier, not a secret). Only the Super Admin gets it. */
  phoneNumberId: string | null;
  /** Who saved it last. Only the Super Admin gets it. */
  updatedByName: string | null;
  canEdit: boolean;
}

export interface SaveSenderRequest {
  status: SenderStatus;
  whatsAppNumber: string | null;
  phoneNumberId: string | null;
}
