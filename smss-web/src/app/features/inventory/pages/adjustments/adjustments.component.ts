import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, SelectComponent, SelectOption, TableColumn, TableComponent } from '../../../../shared/components';
import { AdjustmentFormModalComponent } from '../../components/adjustment-form-modal/adjustment-form-modal.component';
import { InventoryTabsComponent } from '../../components/inventory-tabs/inventory-tabs.component';
import { InvMovement, REASON_LABELS, formatDateTime, formatQty, reasonLabel } from '../../models/inventory.model';
import { InventoryService } from '../../services/inventory.service';

interface AdjustmentRow extends Record<string, unknown> { when: string; item: string; reason: string; change: string; after: string; remarks: string; by: string }

const COLUMNS: TableColumn<AdjustmentRow>[] = [
  { key: 'when', label: 'When' },
  { key: 'item', label: 'Item' },
  { key: 'reason', label: 'Reason' },
  { key: 'change', label: 'Change' },
  { key: 'after', label: 'Balance after' },
  { key: 'remarks', label: 'Remarks' },
  { key: 'by', label: 'By' },
];

const REASON_FILTER: SelectOption[] = [{ value: '', label: 'Any reason' }, ...Object.entries(REASON_LABELS).map(([value, label]) => ({ value, label }))];

/** Every adjustment ever made (damage, loss, expiry, count corrections, opening balances). Append-only: there is no edit and no delete. */
@Component({
  selector: 'app-inventory-adjustments',
  imports: [ReactiveFormsModule, AdjustmentFormModalComponent, ButtonComponent, CardComponent, IconComponent, InputComponent, InventoryTabsComponent, PageHeaderComponent, PaginationComponent, SelectComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../../styles/inventory-page.scss',
  templateUrl: './adjustments.component.html',
})
export class InventoryAdjustmentsComponent implements OnInit {
  private readonly service = inject(InventoryService);
  private readonly toast = inject(ToastService);

  protected readonly breadcrumbs = [{ label: 'Inventory' }, { label: 'Adjustments' }];
  protected readonly columns = COLUMNS;
  protected readonly reasonFilter = REASON_FILTER;

  protected readonly canManage = signal(false);
  protected readonly list = signal<InvMovement[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly adding = signal(false);

  protected readonly reason = new FormControl('', { nonNullable: true });
  protected readonly from = new FormControl('', { nonNullable: true });
  protected readonly to = new FormControl('', { nonNullable: true });

  protected readonly rows = computed<AdjustmentRow[]>(() =>
    this.list().map((m) => ({
      when: formatDateTime(m.createdAt), item: `${m.itemCode} · ${m.itemName}`, reason: reasonLabel(m.reasonCode),
      change: `${m.quantityChange > 0 ? '+' : ''}${formatQty(m.quantityChange)} ${m.unitCode}`, after: `${formatQty(m.quantityAfter)} ${m.unitCode}`, remarks: m.remarks ?? '', by: m.createdByName ?? '',
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
      .adjustments({ reasonCode: this.reason.value, from: this.from.value, to: this.to.value, page: this.page(), pageSize: this.pageSize() })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({ next: (p) => { this.list.set(p.items); this.total.set(p.totalCount); this.canManage.set(p.canManage); }, error: () => this.failed.set(true) });
  }

  protected onPage(p: number): void { this.page.set(p); this.load(); }
  protected onPageSize(s: number): void { this.pageSize.set(s); this.page.set(1); this.load(); }
  protected onAdded(): void { this.adding.set(false); this.page.set(1); this.load(); }
}
