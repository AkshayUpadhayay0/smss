// Mirrors RecordPaymentRequest / PaymentSummaryDto / FeeReceiptDto.

/** The payment modes the API accepts (stored exactly as spelled). */
export const PAYMENT_MODES = ['Cash', 'Cheque', 'UPI', 'Card', 'Bank Transfer', 'Online'] as const;
export type PaymentMode = (typeof PAYMENT_MODES)[number];

export interface PaymentAllocation {
  feeDueId: number;
  amount: number;
}

export interface RecordPaymentRequest {
  amount: number;
  paymentMode: PaymentMode;
  /** The split over installments. Omit it for oldest-first automatic allocation (the dialog always sends one). */
  allocations?: PaymentAllocation[];
  remarks?: string | null;
}

export interface PaymentSummary {
  paymentId: number;
  receiptNumber: string;
  paymentDate: string;
  amount: number;
  paymentMode: string;
  collectedByName?: string | null;
  remarks?: string | null;
  /** How many installments the payment went to. */
  dueCount: number;
  /** A voided payment stays in the history (and its receipt stays viewable) but no longer counts towards any installment. */
  status: 'Active' | 'Voided';
  voidedAt?: string | null;
  voidedByName?: string | null;
  voidReason?: string | null;
}

export interface ReceiptLine {
  feeDueId: number;
  feeTypeName: string;
  frequencyName: string;
  dueDate: string;
  /** The installment's full amount. */
  amountDue: number;
  /** What THIS payment put towards it. */
  allocatedAmount: number;
  /** Everything paid against it so far (this payment included). */
  paidToDate: number;
}

export interface FeeReceipt {
  paymentId: number;
  receiptNumber: string;
  paymentDate: string;
  amount: number;
  paymentMode: string;
  remarks?: string | null;
  collectedByName?: string | null;
  status: 'Active' | 'Voided';
  voidedAt?: string | null;
  voidedByName?: string | null;
  voidReason?: string | null;
  school: { name: string; address?: string | null; phone?: string | null; email?: string | null; logoUrl?: string | null };
  student: { studentId: string; name: string; className?: string | null; academicYearName?: string | null };
  lines: ReceiptLine[];
}

export interface PaymentRow extends Record<string, unknown> {
  paymentId: number;
  receiptNumber: string;
  paymentDate: string;
  amount: string;
  paymentMode: string;
  collectedByName: string;
  status: string;
}

/** Who may void a payment: the same roles the API accepts (it re-checks; this only decides whether to show the button). */
export const FEE_MANAGER_ROLES = ['School Admin', 'Accountant'] as const;

/** Pill colour of a payment's status. */
export const paymentStatusTone = (v: unknown): string => (v === 'Voided' ? 'red' : 'green');

/** Money as whole paise, so sums and comparisons never suffer from floating point (0.1 + 0.2). */
export const toPaise = (value: string | number): number => Math.round(Number(value) * 100);
