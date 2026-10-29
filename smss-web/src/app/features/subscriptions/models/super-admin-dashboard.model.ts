/** Shapes of the Super Admin dashboard and subscription billing endpoints. Dates are yyyy-MM-dd strings; money is rupees. */

export type SubscriptionStatus = 'TRIAL' | 'ACTIVE' | 'OVERDUE' | 'CANCELLED' | 'NOT_SET';
export type BillingFrequency = 'MONTHLY' | 'YEARLY';
export type InvoiceStatus = 'Pending' | 'Paid' | 'Overdue';

export interface SubscriptionPricing {
  pricingId: number;
  ratePerStudentMonthly: number;
  yearlyDiscountPercent: number;
  trialDays: number;
  effectiveFrom: string;
  isActive: boolean;
}

export interface UpcomingRenewal {
  schoolId: string;
  schoolName: string;
  billingFrequency: BillingFrequency | null;
  nextRenewalDate: string;
}

export interface SuperAdminSummary {
  asOf: string;
  totalSchools: number;
  /** Schools whose own (operational) status is Active: not the subscription status. */
  activeSchools: number;
  trialCount: number;
  activeCount: number;
  overdueCount: number;
  cancelledCount: number;
  /** Registered before billing existed, so no subscription row yet. */
  notSetCount: number;
  trialExpiredCount: number;
  totalCollected: number;
  totalOutstanding: number;
  overdueAmount: number;
  upcomingRenewalDays: number;
  upcomingRenewalCount: number;
  upcomingRenewals: UpcomingRenewal[];
  pricing: SubscriptionPricing | null;
}

export interface SchoolSubscriptionRow {
  schoolId: string;
  schoolCode: string;
  schoolName: string;
  schoolStatusId: number | null;
  subscriptionStatus: SubscriptionStatus;
  billingFrequency: BillingFrequency | null;
  trialEndDate: string | null;
  trialExpired: boolean;
  nextRenewalDate: string | null;
  /** The school's own terms (null / false = the global terms). */
  customRatePerStudentMonthly: number | null;
  customYearlyDiscountPercent: number | null;
  includeStaffInBilling: boolean;
  /** Live head counts. */
  studentCount: number;
  teacherCount: number;
  staffCount: number;
  /** Live: what an invoice generated now would bill under the school's terms. */
  billableUserCount: number;
}

/** A school's negotiated terms. Null rate / discount = use the global pricing. */
export interface BillingTerms {
  customRatePerStudentMonthly: number | null;
  customYearlyDiscountPercent: number | null;
  includeStaffInBilling: boolean;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface PaymentAllocation {
  invoiceId: number;
  allocatedAmount: number;
}

export interface SubscriptionPayment {
  paymentId: number;
  schoolId: string;
  paymentDate: string;
  /** The whole transaction; the allocations add up to exactly this. */
  amount: number;
  allocations: PaymentAllocation[];
  paymentMode: string;
  referenceNumber: string | null;
  remarks: string | null;
  status: 'Active' | 'Voided';
  voidedAt: string | null;
  voidReason: string | null;
}

export interface SubscriptionInvoice {
  invoiceId: number;
  schoolId: string;
  schoolName: string;
  billingPeriodStart: string;
  billingPeriodEnd: string;
  billingFrequency: BillingFrequency;
  /** Head counts at generation, all stored whatever was billed. */
  studentCount: number;
  teacherCount: number;
  staffCount: number;
  /** True when teachers and staff were billed as well as students. */
  staffBilled: boolean;
  /** What was priced. */
  billableUserCount: number;
  /** The rate actually used (the school's own or the global one). */
  ratePerStudent: number;
  discountPercent: number;
  grossAmount: number;
  discountAmount: number;
  netAmount: number;
  dueDate: string;
  paidAmount: number;
  balanceAmount: number;
  status: InvoiceStatus;
  /** Only on the single-invoice endpoint: every payment with an allocation to this invoice, voided ones included. */
  payments?: SubscriptionPayment[];
}

export interface RecordPaymentRequest {
  schoolId: string;
  /** The total received. May cover several invoices. */
  amount: number;
  paymentMode: string;
  referenceNumber: string | null;
  remarks: string | null;
  /** Omit to pay the school's oldest unpaid invoices first. Must add up to `amount` exactly. */
  allocations?: { invoiceId: number; amount: number }[];
}

export const PAYMENT_MODES = ['Cash', 'Cheque', 'UPI', 'Card', 'Bank Transfer', 'Online'] as const;

const STATUS_LABELS: Record<SubscriptionStatus, string> = {
  TRIAL: 'Trial',
  ACTIVE: 'Active',
  OVERDUE: 'Overdue',
  CANCELLED: 'Cancelled',
  NOT_SET: 'Not set up',
};

export const subscriptionStatusLabel = (s: SubscriptionStatus): string => STATUS_LABELS[s] ?? s;

/** Pill colour (see the table's tone-* classes) for a subscription status. */
export const subscriptionTone = (s: unknown): string =>
  s === 'ACTIVE' ? 'green' : s === 'TRIAL' ? 'blue' : s === 'OVERDUE' ? 'red' : s === 'CANCELLED' ? 'gray-strike' : 'gray';

/** Pill colour for an invoice status. */
export const invoiceTone = (s: unknown): string => (s === 'Paid' ? 'green' : s === 'Overdue' ? 'red' : 'yellow');
