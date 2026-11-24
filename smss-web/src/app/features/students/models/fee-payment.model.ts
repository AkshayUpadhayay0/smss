// Mirrors RecordPaymentRequest / PaymentSummaryDto / FeeReceiptDto.
import { ConcessionType, concessionNameWithValue } from '../../fee-setup/models/fee-concession.model';
import { taxNameWithRate } from '../../fee-setup/models/tax-type.model';

/** The payment modes the API accepts (stored exactly as spelled). */
export const PAYMENT_MODES = ['Cash', 'Cheque', 'UPI', 'Card', 'Bank Transfer', 'Online'] as const;
export type PaymentMode = (typeof PAYMENT_MODES)[number];

export interface PaymentAllocation {
  feeDueId: number;
  amount: number;
}

/** The fee / tax / total split the API returns on every payment, history row and receipt. Optional so a payment without tax (or older fixtures) reads as before. */
export interface PaymentTaxFields {
  /** The CASH fee: the amount applied to installments minus the concession (equal to `netFeeAmount`). */
  feeAmount?: number;
  /** What was applied to the installments before any concession (= net fee + concession). */
  grossAmount?: number;
  netFeeAmount?: number;
  /** The concession given on this payment, as it was at the time (a later edit never changes it). */
  concessionName?: string | null;
  concessionCode?: string | null;
  concessionType?: ConcessionType | null;
  concessionValue?: number | null;
  concessionAmount?: number;
  /** Name, code and rate as they were when the payment was taken. */
  taxName?: string | null;
  taxCode?: string | null;
  taxRatePercent?: number | null;
  taxAmount?: number;
  /** Fee + tax = what was handed over (same value as `amount`). */
  totalAmount?: number;
}

/** True when the payment carried a tax type (a 0% type still counts: it is on the receipt). */
export const hasTax = (p: PaymentTaxFields): boolean => !!p.taxName;

/** True when the payment carried a concession. */
export const hasConcession = (p: PaymentTaxFields): boolean => !!p.concessionName && (p.concessionAmount ?? 0) > 0;

/** A FULL WAIVER: a concession covered the whole amount, so nothing was collected (the receipt still has its lines and a total of ₹0.00). */
export const isFullyWaived = (p: PaymentTaxFields & { amount: number }): boolean => hasConcession(p) && (p.totalAmount ?? p.amount) === 0;

/** "Sibling 10%" / "Staff ward (₹500)": the concession's name, with its value only when the name does not already state it. */
export const concessionLabel = (p: PaymentTaxFields): string => concessionNameWithValue(p.concessionName ?? '', p.concessionType ?? 'FIXED', p.concessionValue ?? 0);

/** One line of the totals block of a receipt (screen and print share it). */
export interface ReceiptTotalLine {
  label: string;
  amount: number;
  /** Shown as a deduction ("− ₹100.00"). */
  deduction?: boolean;
  /** The last, bold line. */
  total?: boolean;
}

/**
 * The totals of a receipt. Without concession or tax it is the single "Total received" line, exactly as before. With either:
 * Fee amount (gross) / Concession (−) / Net fee / Tax / Total paid, skipping the lines that do not apply (no concession: no Concession and Net fee lines; no tax: no Tax line).
 */
export function receiptTotals(r: PaymentTaxFields & { amount: number }): ReceiptTotalLine[] {
  const taxed = hasTax(r);
  const discounted = hasConcession(r);
  if (!taxed && !discounted) return [{ label: 'Total received', amount: r.amount, total: true }];
  const lines: ReceiptTotalLine[] = [{ label: 'Fee amount', amount: r.grossAmount ?? r.feeAmount ?? 0 }];
  if (discounted) {
    lines.push({ label: `Concession: ${concessionLabel(r)}`, amount: r.concessionAmount ?? 0, deduction: true });
    lines.push({ label: 'Net fee', amount: r.netFeeAmount ?? r.feeAmount ?? 0 });
  }
  if (taxed) lines.push({ label: `Tax: ${taxLabel(r)}`, amount: r.taxAmount ?? 0 });
  lines.push({ label: 'Total paid', amount: r.totalAmount ?? r.amount, total: true });
  return lines;
}

/** "GST (18%)" style description of the tax on a payment. */
export const taxLabel = (p: PaymentTaxFields): string => taxNameWithRate(p.taxName ?? '', p.taxRatePercent ?? 0);

export interface RecordPaymentRequest {
  /** The amount to put against the dues: GROSS, before any concession. The server takes the concession off and adds tax on what is left; no discount, tax or total amount is ever sent. */
  amount: number;
  /** Optional: ONE of the school's active concessions. A written reason is then required. */
  concessionId?: number | null;
  concessionReason?: string | null;
  /** Optional: one of the school's active tax types. */
  taxTypeId?: number | null;
  paymentMode: PaymentMode;
  /** The split over installments. Omit it for oldest-first automatic allocation (the dialog always sends one). */
  allocations?: PaymentAllocation[];
  remarks?: string | null;
}

export interface PaymentSummary extends PaymentTaxFields {
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
  /** Why the concession was given and who applied it. Staff views only: absent on a student's own views. */
  concessionReason?: string | null;
  concessionAppliedByName?: string | null;
}

export interface ReceiptLine {
  /** The part of this line's amount that was discounted (0 when the concession did not apply to it). */
  concessionAmount?: number;
  feeDueId: number;
  /** The fee type's name when the payment was taken (a later rename does not change an old receipt). */
  feeTypeName: string;
  /** The academic year of the installment: a payment can pay installments of more than one year. */
  academicYearName?: string;
  frequencyName: string;
  dueDate: string;
  /** The installment's full amount. */
  amountDue: number;
  /** What THIS payment put towards it. */
  allocatedAmount: number;
  /** Everything paid against it so far (this payment included). */
  paidToDate: number;
}

export interface FeeReceipt extends PaymentTaxFields {
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
  /** Why the concession was given and who applied it. Staff views only: absent on a student's own views. */
  concessionReason?: string | null;
  concessionAppliedByName?: string | null;
  school: { name: string; address?: string | null; phone?: string | null; email?: string | null; logoUrl?: string | null; gstin?: string | null };
  /** className / academicYearName are the student's class IN THE YEAR OF THE PAYMENT'S INSTALLMENTS; academicYearNames lists every year when a payment spans years. */
  student: { studentId: string; name: string; className?: string | null; academicYearName?: string | null; academicYearNames?: string[] };
  lines: ReceiptLine[];
}

export interface PaymentRow extends Record<string, unknown> {
  paymentId: number;
  receiptNumber: string;
  paymentDate: string;
  /** Fee part, tax text ("GST (18%) · ₹180.00" or "—") and the total paid. */
  feeAmount: string;
  tax: string;
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

/** One active concession the person collecting a payment may choose (GET /api/FeePayment/concessions). */
export interface ConcessionChoice {
  concessionId: number;
  concessionName: string;
  concessionCode: string;
  concessionType: ConcessionType;
  value: number;
  /** When set, the concession applies only to installments of this fee type. */
  applicableFeeTypeId?: number | null;
  applicableFeeTypeName?: string | null;
}
