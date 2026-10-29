/** SMSS's own business data (company employees, expenses, salary payments, financial overview). Dates are yyyy-MM-dd strings; money is rupees. */

export interface CompanyEmployee {
  companyEmployeeId: number;
  firstName: string;
  lastName: string;
  fullName: string;
  designation: string;
  email: string | null;
  mobile: string | null;
  dateOfJoining: string;
  dateOfLeaving: string | null;
  /** The current salary: what a new salary payment defaults to. Past payments keep their own amounts. */
  monthlySalary: number;
  statusId: number;
  statusName: 'Active' | 'Inactive';
}

export interface CompanyEmployeeRequest {
  firstName: string;
  lastName: string;
  designation: string;
  email: string | null;
  mobile: string | null;
  dateOfJoining: string;
  dateOfLeaving: string | null;
  monthlySalary: number;
}

export interface ExpenseCategory {
  categoryId: number;
  categoryName: string;
  categoryCode: string;
}

export type RecordStatus = 'Active' | 'Voided';

export interface CompanyExpense {
  expenseId: number;
  expenseDate: string;
  categoryId: number;
  categoryName: string;
  description: string;
  amount: number;
  paymentMode: string;
  recordedByName: string | null;
  status: RecordStatus;
  voidedAt: string | null;
  voidReason: string | null;
}

export interface CreateCompanyExpenseRequest {
  expenseDate: string;
  categoryId: number;
  description: string;
  amount: number;
  paymentMode: string;
}

export interface CompanySalaryPayment {
  salaryPaymentId: number;
  companyEmployeeId: number;
  employeeName: string;
  periodMonth: number;
  periodYear: number;
  amount: number;
  paymentDate: string;
  paymentMode: string;
  remarks: string | null;
  recordedByName: string | null;
  status: RecordStatus;
  voidedAt: string | null;
  voidReason: string | null;
}

export interface RecordCompanySalaryRequest {
  companyEmployeeId: number;
  periodMonth: number;
  periodYear: number;
  /** Omit to pay the employee's current monthly salary. */
  amount?: number | null;
  paymentDate?: string | null;
  paymentMode: string;
  remarks?: string | null;
}

export interface CompanyPaged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  /** Sum of the Active rows matching the filters (all pages). */
  activeTotal: number;
}

export interface FinanceMonth {
  month: string;
  label: string;
  revenue: number;
  expenses: number;
  salaryPayouts: number;
  netProfit: number;
}

export interface CompanyFinanceOverview {
  from: string;
  to: string;
  revenue: number;
  expenses: number;
  salaryPayouts: number;
  netProfit: number;
  months: FinanceMonth[];
}

export const MONTH_NAMES = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'] as const;

/** "Sep 2026" for month 9 of 2026. */
export const periodLabel = (month: number, year: number): string => `${MONTH_NAMES[month - 1]?.slice(0, 3) ?? month} ${year}`;
