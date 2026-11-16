import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, TableColumn, TableComponent } from '../../../../shared/components';
import { InventoryTabsComponent } from '../../components/inventory-tabs/inventory-tabs.component';
import { InvDashboard, formatDate, formatDateTime, formatMoney, formatQty, movementLabel, reasonLabel } from '../../models/inventory.model';
import { InventoryService } from '../../services/inventory.service';

interface StockRow extends Record<string, unknown> { item: string; quantity: string; threshold: string }
interface ExpiryRow extends Record<string, unknown> { item: string; quantity: string; expiry: string }
interface MovementRow extends Record<string, unknown> { when: string; item: string; what: string; change: string; after: string; by: string }

const LOW_COLUMNS: TableColumn<StockRow>[] = [
  { key: 'item', label: 'Item' },
  { key: 'quantity', label: 'On hand' },
  { key: 'threshold', label: 'Minimum' },
];
const EXPIRY_COLUMNS: TableColumn<ExpiryRow>[] = [
  { key: 'item', label: 'Item' },
  { key: 'quantity', label: 'On hand' },
  { key: 'expiry', label: 'Expiry' },
];
const MOVEMENT_COLUMNS: TableColumn<MovementRow>[] = [
  { key: 'when', label: 'When' },
  { key: 'item', label: 'Item' },
  { key: 'what', label: 'Movement' },
  { key: 'change', label: 'Change' },
  { key: 'after', label: 'Balance' },
  { key: 'by', label: 'By' },
];

/**
 * Inventory dashboard: read-only numbers from ONE aggregate call. A part the server could not load comes back null and reads "Unavailable", never a false 0.
 * Action links only for those who can manage (the server sends canManage = false for a Principal).
 */
@Component({
  selector: 'app-inventory-dashboard',
  imports: [RouterLink, ButtonComponent, CardComponent, IconComponent, InventoryTabsComponent, PageHeaderComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/inventory-page.scss', '../../styles/inventory-dashboard.scss'],
  templateUrl: './inventory-dashboard.component.html',
})
export class InventoryDashboardComponent implements OnInit {
  private readonly service = inject(InventoryService);

  protected readonly breadcrumbs = [{ label: 'Inventory' }, { label: 'Dashboard' }];
  protected readonly lowColumns = LOW_COLUMNS;
  protected readonly expiryColumns = EXPIRY_COLUMNS;
  protected readonly movementColumns = MOVEMENT_COLUMNS;
  protected readonly money = formatMoney;

  protected readonly data = signal<InvDashboard | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected readonly lowRows = computed<StockRow[]>(() =>
    (this.data()?.lowStock ?? []).map((i) => ({ item: `${i.itemCode} · ${i.itemName}`, quantity: `${formatQty(i.currentQuantity)} ${i.unitCode}`, threshold: `${formatQty(i.minStockLevel)} ${i.unitCode}` })),
  );
  protected readonly expiringRows = computed<ExpiryRow[]>(() => this.expiryRows(this.data()?.expiringSoon));
  protected readonly expiredRows = computed<ExpiryRow[]>(() => this.expiryRows(this.data()?.expired));
  protected readonly movementRows = computed<MovementRow[]>(() =>
    (this.data()?.recentMovements ?? []).map((m) => ({
      when: formatDateTime(m.createdAt),
      item: `${m.itemCode} · ${m.itemName}`,
      what: m.reasonCode && m.movementType === 'ADJUSTMENT' ? `${movementLabel(m.movementType)}: ${reasonLabel(m.reasonCode)}` : movementLabel(m.movementType),
      change: `${m.quantityChange > 0 ? '+' : ''}${formatQty(m.quantityChange)} ${m.unitCode}`,
      after: `${formatQty(m.quantityAfter)} ${m.unitCode}`,
      by: m.createdByName ?? '',
    })),
  );

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.service
      .dashboard()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({ next: (d) => this.data.set(d), error: () => this.failed.set(true) });
  }

  /** A count that may be missing: the number, or the word "Unavailable". */
  protected count(n: number | null | undefined): string {
    return n == null ? 'Unavailable' : String(n);
  }

  private expiryRows(list: InvDashboard['expiringSoon'] | undefined): ExpiryRow[] {
    return (list ?? []).map((i) => ({ item: `${i.itemCode} · ${i.itemName}`, quantity: `${formatQty(i.currentQuantity)} ${i.unitCode}`, expiry: formatDate(i.expiryDate) }));
  }
}
