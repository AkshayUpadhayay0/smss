// Inventory & Stock, Stage 1: shapes of the API responses (smss-api-service-layer/dto/inventory/InventoryDtos.cs).
// Nothing here carries a quantity INPUT for an item: stock changes only through stock-in, adjustments, issue and return.

export interface InvList<T> { canManage: boolean; items: T[] }
export interface InvPage<T> { canManage: boolean; items: T[]; totalCount: number; page: number; pageSize: number }

export interface InvCategory { categoryId: number; categoryName: string; categoryCode: string; description: string | null; isActive: boolean; itemCount: number }
export interface SaveInvCategory { categoryName: string; categoryCode: string; description: string | null }

export interface InvSupplier {
  supplierId: number; supplierName: string; contactPerson: string | null; mobile: string | null; email: string | null; address: string | null;
  gstin: string | null; pan: string | null; isActive: boolean;
}
export interface SaveInvSupplier { supplierName: string; contactPerson: string | null; mobile: string | null; email: string | null; address: string | null; gstin: string | null; pan: string | null }

export interface InvItem {
  itemId: number; itemCode: string; barcode: string | null; itemName: string; description: string | null;
  categoryId: number; categoryName: string; unitId: number; unitName: string; unitCode: string; itemType: 'CONSUMABLE' | 'ASSET';
  purchasePrice: number; sellingPrice: number | null; isSaleable: boolean; gstPercent: number; minStockLevel: number; currentQuantity: number; stockValue: number;
  expiryDate: string | null; hasPhoto: boolean; isActive: boolean; isLowStock: boolean; isExpired: boolean; isExpiringSoon: boolean;
}

/** There is deliberately no quantity here. */
export interface SaveInvItem {
  itemName: string; barcode: string | null; description: string | null; categoryId: number; unitId: number; itemType: string;
  purchasePrice: number; sellingPrice: number | null; isSaleable: boolean; gstPercent: number; minStockLevel: number; expiryDate: string | null;
}

export interface InvUnit { unitId: number; unitName: string; unitCode: string }
export interface InvReason { code: string; label: string; direction: 'IN' | 'OUT' | 'EITHER' }

export interface InvStockIn {
  stockInId: number; itemId: number; itemCode: string; itemName: string; unitCode: string; supplierId: number | null; supplierName: string | null;
  quantity: number; unitCost: number; totalCost: number; receivedDate: string; billNumber: string | null; expiryDate: string | null; remarks: string | null;
  receivedByName: string | null; status: 'Active' | 'Voided'; voidedByName: string | null; voidedAt: string | null; voidReason: string | null; createdAt: string;
}
export interface CreateInvStockIn { itemId: number; supplierId: number | null; quantity: number; unitCost: number; receivedDate: string; billNumber: string | null; expiryDate: string | null; remarks: string | null }

export interface InvMovement {
  movementId: number; itemId: number; itemCode: string; itemName: string; unitCode: string; movementType: string; quantityChange: number; quantityAfter: number;
  reasonCode: string | null; referenceType: string | null; referenceId: number | null; unitCost: number | null; remarks: string | null; createdByName: string | null; createdAt: string;
}
export interface CreateInvAdjustment { itemId: number; reasonCode: string; quantityChange: number; remarks: string }

export interface InvReturn { returnId: number; quantity: number; returnDate: string; condition: 'GOOD' | 'DAMAGED'; remarks: string | null; receivedByName: string | null }
export interface InvIssueLine {
  issueLineId: number; itemId: number; itemCode: string; itemName: string; unitCode: string; quantity: number; returnedQuantity: number; outstandingQuantity: number; returns: InvReturn[];
}
export type InvIssueStatus = 'Outstanding' | 'Partially Returned' | 'Returned' | 'Voided';
export type InvRecipientType = 'STAFF' | 'STUDENT' | 'DEPARTMENT';
export interface InvIssue {
  issueId: number; issueNumber: string; recipientType: InvRecipientType; recipientId: string; recipientName: string; issueDate: string; purpose: string | null;
  issuedByName: string | null; status: InvIssueStatus; totalQuantity: number; returnedQuantity: number; voidedByName: string | null; voidedAt: string | null; voidReason: string | null;
  createdAt: string; lines: InvIssueLine[];
}
export interface CreateInvIssue { recipientType: InvRecipientType; recipientId: string; issueDate: string; purpose: string | null; lines: { itemId: number; quantity: number }[] }
export interface InvReturnBody { returnDate: string; lines: { issueLineId: number; quantity: number; condition: string; remarks: string | null }[] }
export interface InvRecipient { id: string; name: string; detail: string | null }

export interface InvDashboardItem { itemId: number; itemCode: string; itemName: string; unitCode: string; currentQuantity: number; minStockLevel: number; expiryDate: string | null }
/** Every part may be null: it could not be loaded, and the screen says "Unavailable" instead of a false 0. */
export interface InvDashboard {
  canManage: boolean; expiringSoonDays: number; totalActiveItems: number | null; totalStockValue: number | null;
  lowStockCount: number | null; lowStock: InvDashboardItem[] | null; expiringSoonCount: number | null; expiringSoon: InvDashboardItem[] | null;
  expiredCount: number | null; expired: InvDashboardItem[] | null; issuedNotReturnedCount: number | null; recentMovements: InvMovement[] | null;
  stockInsLast30Days: number | null; issuesLast30Days: number | null; unavailableParts: string[];
}

// ---------------------------------------------------------------- display helpers
/** A quantity without trailing zeros: 5, 2.5, 0.125. */
export const formatQty = (n: number | null | undefined): string =>
  n == null ? '—' : Number(n).toLocaleString('en-IN', { maximumFractionDigits: 3 });

export const formatMoney = (n: number | null | undefined): string =>
  n == null ? '—' : Number(n).toLocaleString('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 2 });

export const formatDate = (iso: string | null | undefined): string => {
  if (!iso) return '—';
  const [y, m, d] = iso.slice(0, 10).split('-').map(Number);
  return new Date(y, m - 1, d).toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' });
};

export const formatDateTime = (iso: string | null | undefined): string =>
  iso ? new Date(iso).toLocaleString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—';

/** The one stock label of an item, worst first: Expired, Out of stock, Low stock, Expiring soon, In stock. */
export function stockLabel(i: Pick<InvItem, 'currentQuantity' | 'isLowStock' | 'isExpired' | 'isExpiringSoon'>): string {
  if (i.isExpired) return 'Expired';
  if (i.currentQuantity <= 0) return 'Out of stock';
  if (i.isLowStock) return 'Low stock';
  if (i.isExpiringSoon) return 'Expiring soon';
  return 'In stock';
}

export const stockTone = (label: unknown): string =>
  label === 'Expired' || label === 'Out of stock' ? 'red' : label === 'Low stock' || label === 'Expiring soon' ? 'yellow' : label === 'In stock' ? 'green' : 'gray';

export const issueTone = (status: unknown): string =>
  status === 'Returned' ? 'green' : status === 'Partially Returned' ? 'blue' : status === 'Outstanding' ? 'yellow' : status === 'Voided' ? 'gray-strike' : 'gray';

export const MOVEMENT_LABELS: Record<string, string> = {
  OPENING: 'Opening balance', STOCK_IN: 'Stock in', ADJUSTMENT: 'Adjustment', ISSUE: 'Issued', RETURN: 'Returned', STOCK_IN_VOID: 'Stock-in voided', ISSUE_VOID: 'Issue voided',
};
export const movementLabel = (type: string): string => MOVEMENT_LABELS[type] ?? type;

export const REASON_LABELS: Record<string, string> = {
  OPENING_BALANCE: 'Opening balance', DAMAGE: 'Damaged', LOSS: 'Lost', EXPIRED: 'Expired', CORRECTION: 'Count correction',
};
export const reasonLabel = (code: string | null): string => (code ? (REASON_LABELS[code] ?? code) : '');

export const RECIPIENT_LABELS: Record<InvRecipientType, string> = { STAFF: 'Staff', STUDENT: 'Student', DEPARTMENT: 'Department' };

/** The signed change to send for an adjustment, from what the person typed (always a positive amount) and the reason. */
export function signedAdjustment(reason: string, amount: number, correctionDirection: 'IN' | 'OUT'): number {
  const out = reason === 'DAMAGE' || reason === 'LOSS' || reason === 'EXPIRED' || (reason === 'CORRECTION' && correctionDirection === 'OUT');
  return out ? -Math.abs(amount) : Math.abs(amount);
}

/** Today as yyyy-MM-dd in the browser's calendar (the server checks against the school's India day). */
export const todayIso = (): string => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};

/** A quantity typed in a form: positive, at most 3 decimals. Returns an error text or null. */
export function quantityError(raw: unknown): string | null {
  const s = String(raw ?? '').trim();
  if (s === '') return null; // `required` reports blanks
  const n = Number(s);
  if (!Number.isFinite(n)) return 'Enter a number';
  if (n <= 0) return 'Must be greater than 0';
  if (!/^\d+(\.\d{1,3})?$/.test(s)) return 'Use at most 3 decimal places';
  return null;
}

/** Money typed in a form: 0 or more, at most 2 decimals. */
export function moneyError(raw: unknown): string | null {
  const s = String(raw ?? '').trim();
  if (s === '') return null;
  const n = Number(s);
  if (!Number.isFinite(n)) return 'Enter a number';
  if (n < 0) return 'Can not be negative';
  if (!/^\d+(\.\d{1,2})?$/.test(s)) return 'Use at most 2 decimal places';
  return null;
}
