import { concessionPaise } from '../../fee-setup/models/fee-concession.model';
import { TaxType, taxPaise } from '../../fee-setup/models/tax-type.model';
import { ConcessionChoice, toPaise } from '../models/fee-payment.model';

/**
 * The money preview of the Collect Payment dialog. It is only a PREVIEW: the server recomputes everything with the same rules (ROUND_HALF_UP to 2 decimals,
 * discount first, then tax on the net fee) and is the source of truth; the request carries the allocations, the concession id and reason and the tax type id, never an amount of
 * discount, tax or total. All arithmetic is in whole paise, so there is no floating point drift, and nothing here can be negative or NaN.
 */

export interface PayRow {
  dueId: number;
  /** The installment's fee type (null on an older server). */
  feeTypeId: number | null;
  feeTypeName: string;
  /** Still owed on the installment, in paise. */
  outstanding: number;
  checked: boolean;
  /** What the cashier typed for this installment, in rupees. */
  amount: string;
  /** Set on an installment of an EARLIER academic year (listed first, pre-ticked, still optional). */
  yearName?: string;
}

export const MIN_PAISE = 1;
const MONEY = /^\d+(\.\d{1,2})?$/;

/** The text of a paise amount as rupees with 2 decimals ("1500" paise -> "15.00"). */
export const rupees = (paise: number): string => (Math.max(paise, 0) / 100).toFixed(2);

/** Whether `text` is a well-formed amount: digits with at most 2 decimals. */
export const isMoney = (text: string): boolean => MONEY.test(text.trim());

/** The error for a ticked installment's amount, or null: 0.01 at least, at most what is outstanding, at most 2 decimals. */
export function rowError(r: PayRow): string | null {
  if (!r.checked) return null;
  const t = String(r.amount ?? '').trim();
  if (t === '' || !isMoney(t)) return t === '' ? 'Enter an amount' : 'Use at most 2 decimal places';
  const p = toPaise(t);
  if (p < MIN_PAISE) return 'At least ₹0.01';
  if (p > r.outstanding) return `At most ${'₹' + (r.outstanding / 100).toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} (what is outstanding)`;
  return null;
}

/** The ticked installments with a valid amount, as (row, paise). Rows with an error are left out so they can never inflate the total. */
export function selectedLines(rows: readonly PayRow[]): { row: PayRow; paise: number }[] {
  return rows.filter((r) => r.checked && !rowError(r)).map((r) => ({ row: r, paise: toPaise(r.amount) }));
}

/** Does the concession apply to at least one selected line (all of them when it has no fee type)? */
export function concessionApplies(c: ConcessionChoice, rows: readonly PayRow[]): boolean {
  return selectedLines(rows).some((l) => c.applicableFeeTypeId == null || l.row.feeTypeId === c.applicableFeeTypeId);
}

export interface Preview {
  /** The sum of the selected lines: what goes against the installments. */
  grossPaise: number;
  /** The sum of the selected lines the concession applies to. */
  baseConcessionPaise: number;
  concessionPaise: number;
  /** gross - concession, never below 0. */
  netPaise: number;
  taxPaise: number;
  /** net + tax: what the student pays. */
  totalPaise: number;
  /** The concession covers everything selected: nothing is collected. */
  waived: boolean;
}

export function buildPreview(rows: readonly PayRow[], concession: ConcessionChoice | null, tax: TaxType | null): Preview {
  const lines = selectedLines(rows);
  const gross = lines.reduce((s, l) => s + l.paise, 0);
  const base = concession ? lines.filter((l) => concession.applicableFeeTypeId == null || l.row.feeTypeId === concession.applicableFeeTypeId).reduce((s, l) => s + l.paise, 0) : 0;
  // never more than the base (and the base is never more than the gross), so the net fee can't go below 0
  const discount = concession && base > 0 ? Math.min(concessionPaise(concession.concessionType, concession.value, base), base, gross) : 0;
  const net = Math.max(gross - discount, 0);
  const taxP = tax && net > 0 ? taxPaise(net, tax.ratePercent) : 0;
  return { grossPaise: gross, baseConcessionPaise: base, concessionPaise: discount, netPaise: net, taxPaise: taxP, totalPaise: net + taxP, waived: !!concession && discount > 0 && net === 0 };
}

/** The result of checking what the cashier says they received. */
export interface ReceivedCheck {
  ok: boolean;
  error: string | null;
  /** Cash only: what to hand back, in paise (0 when nothing). */
  changePaise: number;
}

/** Cash: at least the total (change is returned). Every other mode: exactly the total. A fully waived payment has nothing to receive. */
export function checkReceived(text: string, totalPaise: number, mode: string, waived: boolean): ReceivedCheck {
  if (waived) return { ok: true, error: null, changePaise: 0 };
  const t = text.trim();
  if (t === '') return { ok: false, error: 'Enter the amount received', changePaise: 0 };
  if (!isMoney(t)) return { ok: false, error: 'Use at most 2 decimal places', changePaise: 0 };
  const got = toPaise(t);
  if (mode === 'Cash') {
    return got >= totalPaise ? { ok: true, error: null, changePaise: got - totalPaise } : { ok: false, error: `Cash received must be at least ₹${rupees(totalPaise)}`, changePaise: 0 };
  }
  return got === totalPaise ? { ok: true, error: null, changePaise: 0 } : { ok: false, error: `For ${mode} the amount must be exactly ₹${rupees(totalPaise)}`, changePaise: 0 };
}

/** Spread an amount over the installments oldest first (rows are already in due-date order). Returns the new rows, and what could not be placed. */
export function allocateOldestFirst(rows: readonly PayRow[], amountPaise: number): { rows: PayRow[]; leftPaise: number } {
  let left = amountPaise;
  const next = rows.map((r) => {
    const take = Math.min(Math.max(left, 0), r.outstanding);
    left -= take;
    return take > 0 ? { ...r, checked: true, amount: rupees(take) } : { ...r, checked: false, amount: rupees(r.outstanding) };
  });
  return { rows: next, leftPaise: Math.max(left, 0) };
}
