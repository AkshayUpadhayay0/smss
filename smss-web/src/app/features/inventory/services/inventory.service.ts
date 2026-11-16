import { Injectable, inject, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import {
  CreateInvAdjustment, CreateInvIssue, CreateInvStockIn, InvCategory, InvDashboard, InvIssue, InvItem, InvList, InvMovement, InvPage, InvReason, InvRecipient, InvRecipientType,
  InvReturnBody, InvStockIn, InvSupplier, InvUnit, SaveInvCategory, SaveInvItem, SaveInvSupplier,
} from '../models/inventory.model';

type Params = Record<string, string | number | boolean>;

/** Drops the empty filters so they are not sent as "?search=". */
export const cleanParams = (p: Record<string, string | number | boolean | null | undefined>): Params =>
  Object.fromEntries(Object.entries(p).filter(([, v]) => v !== null && v !== undefined && v !== '' && v !== false)) as Params;

export interface ItemQuery { categoryId?: number | null; search?: string; status?: 'active' | 'inactive' | 'all'; lowStock?: boolean; expiring?: boolean; page: number; pageSize: number }
export interface StockInQuery { itemId?: number | null; supplierId?: number | null; from?: string; to?: string; includeVoided?: boolean; page: number; pageSize: number }
export interface AdjustmentQuery { itemId?: number | null; reasonCode?: string; from?: string; to?: string; page: number; pageSize: number }
export interface IssueQuery { recipientType?: string; status?: string; search?: string; from?: string; to?: string; page: number; pageSize: number }

/** Always the caller's own school: the server takes it from the token. Quantities change only through the stock-in / adjustment / issue / return calls. */
@Injectable({ providedIn: 'root' })
export class InventoryService {
  private readonly api = inject(ApiService);

  // ---- dashboard
  dashboard(): Observable<InvDashboard> { return this.api.get<InvDashboard>('/api/InventoryDashboard/summary'); }

  // ---- categories
  categories(): Observable<InvList<InvCategory>> { return this.api.get<InvList<InvCategory>>('/api/InventoryCategory', { includeInactive: true }); }
  saveCategory(id: number | null, body: SaveInvCategory): Observable<InvCategory> {
    return id == null ? this.api.post<InvCategory>('/api/InventoryCategory', body) : this.api.put<InvCategory>(`/api/InventoryCategory/${id}`, body);
  }
  toggleCategory(id: number): Observable<InvCategory> { return this.api.post<InvCategory>(`/api/InventoryCategory/${id}/toggle-status`); }

  // ---- suppliers
  suppliers(includeInactive = true): Observable<InvList<InvSupplier>> { return this.api.get<InvList<InvSupplier>>('/api/InventorySupplier', { includeInactive }); }
  saveSupplier(id: number | null, body: SaveInvSupplier): Observable<InvSupplier> {
    return id == null ? this.api.post<InvSupplier>('/api/InventorySupplier', body) : this.api.put<InvSupplier>(`/api/InventorySupplier/${id}`, body);
  }
  toggleSupplier(id: number): Observable<InvSupplier> { return this.api.post<InvSupplier>(`/api/InventorySupplier/${id}/toggle-status`); }

  // ---- items
  items(q: ItemQuery): Observable<InvPage<InvItem>> {
    return this.api.get<InvPage<InvItem>>('/api/InventoryItem', cleanParams({ categoryId: q.categoryId, search: q.search?.trim(), status: q.status ?? 'active', lowStock: q.lowStock, expiring: q.expiring, page: q.page, pageSize: q.pageSize }));
  }
  item(id: number): Observable<InvItem> { return this.api.get<InvItem>(`/api/InventoryItem/${id}`); }
  saveItem(id: number | null, body: SaveInvItem): Observable<InvItem> {
    return id == null ? this.api.post<InvItem>('/api/InventoryItem', body) : this.api.put<InvItem>(`/api/InventoryItem/${id}`, body);
  }
  toggleItem(id: number): Observable<InvItem> { return this.api.post<InvItem>(`/api/InventoryItem/${id}/toggle-status`); }
  uploadPhoto(id: number, file: File): Observable<InvItem> { return this.api.upload<InvItem>(`/api/InventoryItem/${id}/photo`, file, 'file', { skipErrorToast: true }); }
  removePhoto(id: number): Observable<InvItem> { return this.api.post<InvItem>(`/api/InventoryItem/${id}/photo/remove`); }
  /** Bumped whenever an item's photo is replaced or removed, so the next fetch is a new cache entry (the secure image cache is keyed by path). */
  private readonly photoVersions = signal<Record<number, number>>({});
  bumpPhoto(id: number): void { this.photoVersions.update((v) => ({ ...v, [id]: (v[id] ?? 0) + 1 })); }
  /** The authenticated photo URL, shown through `<app-secure-img>` (never an <img src> of its own). Reads a signal, so a bump refreshes the picture. */
  photoPath(id: number): string { return `/api/InventoryItem/${id}/photo?v=${this.photoVersions()[id] ?? 0}`; }

  // ---- stock in
  stockIns(q: StockInQuery): Observable<InvPage<InvStockIn>> {
    return this.api.get<InvPage<InvStockIn>>('/api/InventoryStockIn', cleanParams({ itemId: q.itemId, supplierId: q.supplierId, from: q.from, to: q.to, includeVoided: q.includeVoided, page: q.page, pageSize: q.pageSize }));
  }
  createStockIn(body: CreateInvStockIn): Observable<InvStockIn> { return this.api.post<InvStockIn>('/api/InventoryStockIn', body); }
  voidStockIn(id: number, reason: string): Observable<InvStockIn> { return this.api.post<InvStockIn>(`/api/InventoryStockIn/${id}/void`, { reason }); }

  // ---- adjustments and the ledger
  adjustments(q: AdjustmentQuery): Observable<InvPage<InvMovement>> {
    return this.api.get<InvPage<InvMovement>>('/api/InventoryStockAdjustment', cleanParams({ itemId: q.itemId, reasonCode: q.reasonCode, from: q.from, to: q.to, page: q.page, pageSize: q.pageSize }));
  }
  createAdjustment(body: CreateInvAdjustment): Observable<InvMovement> { return this.api.post<InvMovement>('/api/InventoryStockAdjustment', body); }
  movements(itemId: number, page: number, pageSize: number): Observable<InvPage<InvMovement>> {
    return this.api.get<InvPage<InvMovement>>('/api/InventoryStockMovement', { itemId, page, pageSize });
  }

  // ---- issue / return
  issues(q: IssueQuery): Observable<InvPage<InvIssue>> {
    return this.api.get<InvPage<InvIssue>>('/api/InventoryIssue', cleanParams({ recipientType: q.recipientType, status: q.status, search: q.search?.trim(), from: q.from, to: q.to, page: q.page, pageSize: q.pageSize }));
  }
  issue(id: number): Observable<InvIssue> { return this.api.get<InvIssue>(`/api/InventoryIssue/${id}`); }
  createIssue(body: CreateInvIssue): Observable<InvIssue> { return this.api.post<InvIssue>('/api/InventoryIssue', body); }
  returnIssue(id: number, body: InvReturnBody): Observable<InvIssue> { return this.api.post<InvIssue>(`/api/InventoryIssue/${id}/return`, body); }
  voidIssue(id: number, reason: string): Observable<InvIssue> { return this.api.post<InvIssue>(`/api/InventoryIssue/${id}/void`, { reason }); }

  // ---- lookups
  units(): Observable<InvUnit[]> { return this.api.get<InvUnit[]>('/api/InventoryLookup/units'); }
  reasons(): Observable<InvReason[]> { return this.api.get<InvReason[]>('/api/InventoryLookup/adjustment-reasons'); }
  recipients(type: InvRecipientType, q: string): Observable<InvRecipient[]> { return this.api.get<InvRecipient[]>('/api/InventoryLookup/recipients', cleanParams({ type, q: q.trim() })); }
}
