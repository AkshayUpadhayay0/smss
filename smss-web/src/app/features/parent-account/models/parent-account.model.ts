// Mirrors ParentAccountDtos.cs. No password, token or link is ever part of any of these.

export interface ParentDelivery {
  deliveryId: number;
  channel: 'WHATSAPP' | 'EMAIL';
  status: 'LOGGED' | 'SENT' | 'DELIVERED' | 'READ' | 'FAILED' | 'SKIPPED';
  templateCode: string;
  purpose: string;
  recipientMasked?: string | null;
  senderKind: string;
  errorCode?: string | null;
  errorMessage?: string | null;
  createdAt: string;
}

/** CREATED, CHILD_ADDED, RESENT, NOT_CREATED or FAILED. */
export interface ParentAccountResult {
  outcome: 'CREATED' | 'CHILD_ADDED' | 'RESENT' | 'NOT_CREATED' | 'FAILED';
  reason?: string | null;
  guardianId?: number | null;
  loginId?: string | null;
  canResend: boolean;
  deliveries: ParentDelivery[];
}

export interface ParentAccountStatus {
  guardianId: number;
  guardianName: string;
  hasAccount: boolean;
  loginId?: string | null;
  passwordSet: boolean;
  consentRecorded: boolean;
  whatsAppOnFile: boolean;
  emailOnFile: boolean;
  linksInLast24Hours: number;
  maxLinksPer24Hours: number;
  deliveries: ParentDelivery[];
}

export interface BulkParentAccountPreview {
  eligible: number;
  willCreate: number;
  maxPerRun: number;
  incompleteDetails: number;
  withoutConsentOnRecord: number;
}

export interface BulkParentAccountItem {
  guardianId: number;
  name: string;
  outcome: string;
  reason?: string | null;
}

export interface BulkParentAccountResult {
  attempted: number;
  created: number;
  notCreated: number;
  remaining: number;
  items: BulkParentAccountItem[];
}

export interface LinkCheck { valid: boolean }
export interface PasswordSet { loginId: string }

/** Plain words for a delivery, for the staff screens. */
export function deliveryLabel(d: Pick<ParentDelivery, 'channel' | 'status' | 'errorMessage'>): string {
  const channel = d.channel === 'WHATSAPP' ? 'WhatsApp' : 'Email';
  switch (d.status) {
    case 'LOGGED': return `${channel}: recorded only (WhatsApp sending is switched off, nothing was sent)`;
    case 'SENT': return `${channel}: sent`;
    case 'DELIVERED': return `${channel}: delivered`;
    case 'READ': return `${channel}: read`;
    case 'SKIPPED': return `${channel}: not sent${d.errorMessage ? ' (' + d.errorMessage + ')' : ''}`;
    default: return `${channel}: failed${d.errorMessage ? ' (' + d.errorMessage + ')' : ''}`;
  }
}

/** One line for a toast after an admission / action: what happened to the parent account. */
export function accountSummary(r: ParentAccountResult): { kind: 'success' | 'warning'; text: string } {
  const failed = r.deliveries.some((d) => d.status === 'FAILED');
  const sent = r.deliveries.some((d) => d.status === 'SENT' || d.status === 'LOGGED');
  switch (r.outcome) {
    case 'CREATED':
      if (r.deliveries.length === 0) return { kind: 'warning', text: `Parent account created (login ID ${r.loginId}), but no link could be sent. ${r.reason ?? ''}`.trim() };
      return failed || !sent
        ? { kind: 'warning', text: `Parent account created (login ID ${r.loginId}), but the link could not be sent. Use Resend on the student's profile.` }
        : { kind: 'success', text: `Parent account created (login ID ${r.loginId}). A link to choose a password was sent.` };
    case 'CHILD_ADDED':
      return { kind: 'success', text: 'The parent already has an account: they were told the child was added. No second account was made.' };
    case 'NOT_CREATED':
      return { kind: 'warning', text: `No parent account was created. ${r.reason ?? ''}`.trim() };
    default:
      return { kind: 'warning', text: r.reason ?? 'The parent account could not be created. The admission is saved; use Resend on the student\'s profile.' };
  }
}
