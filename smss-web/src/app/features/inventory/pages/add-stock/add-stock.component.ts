import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, TableColumn, TableComponent } from '../../../../shared/components';
import { InventoryTabsComponent } from '../../components/inventory-tabs/inventory-tabs.component';
import { StockInFormModalComponent } from '../../components/stock-in-form-modal/stock-in-form-modal.component';
import { VoidReasonModalComponent } from '../../components/void-reason-modal/void-reason-modal.component';
import { InvStockIn, formatDate, formatDateTime, formatMoney, formatQty } from '../../models/inventory.model';
import { InventoryService } from '../../services/inventory.service';

interface StockInRow extends Record<string, unknown> {
  stockInId: number; receivedDate: string; item: string; quantity: string; cost: string; total: string; supplier: string; bill: string; by: string; statusName: string; isActive: boolean;
}

const COLUMNS: TableColumn<StockInRow>[] = [
  { key: 'receivedDate', label: 'Received' },
  { key: 'item', label: 'Item' },
  { key: 'quantity', label: 'Quantity' },
  { key: 'cost', label: 'Unit cost' },
  { key: 'total', label: 'Total' },
  { key: 'supplier', label: 'Supplier' },
  { key: 'bill', label: 'Bill no.' },
  { key: 'by', label: 'Received by' },
  { key: 'statusName', label: 'Status', type: 'badge', tone: (v) => (v === 'Voided' ? 'gray-strike' : 'green') },
];

/** The stock-in log (no purchase order needed). Voiding keeps the row and takes the stock back through the ledger; refused when the stock has already been used. */
@Component({
  selector: 'app-inventory-add-stock',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, InventoryTabsComponent, PageHeaderComponent, PaginationComponent, StockInFormModalComponent, TableComponent, VoidReasonModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../../styles/inventory-page.scss',
  templateUrl: './add-stock.component.html',
})
export class InventoryAddStockComponent implements OnInit {
  private readonly service = inject(InventoryService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);

  protected readonly breadcrumbs = [{ label: 'Inventory' }, { label: 'Add Stock' }];
  protected readonly columns = COLUMNS;

  protected readonly canManage = signal(false);
  protected readonly list = signal<InvStockIn[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly adding = signal(false);
  protected readonly voiding = signal<InvStockIn | null>(null);
  protected readonly voidBusy = signal(false);

  protected readonly from = new FormControl('', { nonNullable: true });
  protected readonly to = new FormControl('', { nonNullable: true });
  protected readonly includeVoided = new FormControl(false, { nonNullable: true });

  protected readonly rows = computed<StockInRow[]>(() =>
    this.list().map((s) => ({
      stockInId: s.stockInId, receivedDate: formatDate(s.receivedDate), item: `${s.itemCode} · ${s.itemName}`, quantity: `${formatQty(s.quantity)} ${s.unitCode}`,
      cost: formatMoney(s.unitCost), total: formatMoney(s.totalCost), supplier: s.supplierName ?? '—', bill: s.billNumber ?? '—', by: s.receivedByName ?? '',
      statusName: s.status, isActive: s.status === 'Active',
    })),
  );

  ngOnInit(): void {
    this.load();
  }

  protected apply(): void {
    if (this.from.value && this.to.value && this.to.value < this.from.value) {
      this.toast.danger("The 'to' date can't be before the 'from' date.");
      return;
    }
    this.page.set(1);
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.service
      .stockIns({ from: this.from.value, to: this.to.value, includeVoided: this.includeVoided.value, page: this.page(), pageSize: this.pageSize() })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({ next: (p) => { this.list.set(p.items); this.total.set(p.totalCount); this.canManage.set(p.canManage); }, error: () => this.failed.set(true) });
  }

  protected onPage(p: number): void { this.page.set(p); this.load(); }
  protected onPageSize(s: number): void { this.pageSize.set(s); this.page.set(1); this.load(); }
  protected onAdded(): void { this.adding.set(false); this.page.set(1); this.load(); }

  /** Step 1 of a void: the confirm dialog. Step 2 (only if confirmed): the reason. */
  protected async startVoid(row: StockInRow): Promise<void> {
    const entry = this.list().find((s) => s.stockInId === row.stockInId);
    if (!entry) return;
    const ok = await this.confirmDialog.confirm({
      title: 'Void this stock-in?',
      message: `This takes ${formatQty(entry.quantity)} ${entry.unitCode} of "${entry.itemName}" back out of stock. The entry stays in the log, marked Voided. It is refused if that stock has already been used.`,
      confirmText: 'Continue',
      variant: 'danger',
    });
    if (ok) this.voiding.set(entry);
  }

  protected confirmVoid(reason: string): void {
    const entry = this.voiding();
    if (!entry || this.voidBusy()) return;
    this.voidBusy.set(true);
    this.service
      .voidStockIn(entry.stockInId, reason)
      .pipe(finalize(() => this.voidBusy.set(false)))
      .subscribe({
        next: () => { this.toast.success('Stock-in voided and the stock taken back.'); this.voiding.set(null); this.load(); },
        error: () => undefined,   // toasted (e.g. 409: only N on hand); the reason form stays open so nothing typed is lost
      });
  }

  protected voidedNote(row: StockInRow): string {
    const s = this.list().find((x) => x.stockInId === row.stockInId);
    return s?.status === 'Voided' ? `Voided ${formatDateTime(s.voidedAt)} by ${s.voidedByName ?? 'unknown'}: ${s.voidReason ?? ''}` : '';
  }
}
