import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { debounceTime, distinctUntilChanged, finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, SelectComponent, SelectOption, TableColumn, TableComponent } from '../../../../shared/components';
import { InventoryTabsComponent } from '../../components/inventory-tabs/inventory-tabs.component';
import { ItemFormModalComponent } from '../../components/item-form-modal/item-form-modal.component';
import { ItemHistoryModalComponent } from '../../components/item-history-modal/item-history-modal.component';
import { InvCategory, InvItem, InvUnit, formatDate, formatMoney, formatQty, stockLabel, stockTone } from '../../models/inventory.model';
import { InventoryService } from '../../services/inventory.service';
import { CategoriesPanelComponent } from './categories-panel/categories-panel.component';

interface ItemRow extends Record<string, unknown> {
  itemId: number; photo: string; itemCode: string; itemName: string; categoryName: string; onHand: string; minimum: string; price: string; expiry: string; stock: string; statusName: string; isActive: boolean;
}

const COLUMNS: TableColumn<ItemRow>[] = [
  { key: 'photo', label: '', type: 'avatar', width: '56px', avatarLabel: (r) => r.itemName },
  { key: 'itemCode', label: 'Code' },
  { key: 'itemName', label: 'Item' },
  { key: 'categoryName', label: 'Category' },
  { key: 'onHand', label: 'On hand' },
  { key: 'minimum', label: 'Minimum' },
  { key: 'price', label: 'Cost' },
  { key: 'expiry', label: 'Expiry' },
  { key: 'stock', label: 'Stock', type: 'badge', tone: stockTone },
  { key: 'statusName', label: 'Status', type: 'status' },
];

const STATUS_OPTIONS: SelectOption[] = [{ value: 'active', label: 'Active items' }, { value: 'inactive', label: 'Inactive items' }, { value: 'all', label: 'All items' }];
const STOCK_OPTIONS: SelectOption[] = [{ value: '', label: 'Any stock level' }, { value: 'low', label: 'Low stock' }, { value: 'expiring', label: 'Expiring soon or expired' }];

/**
 * Items and categories. The item list is paged and filtered by the server. A Principal sees the same lists with no action buttons: every write control
 * depends on the `canManage` flag the server returns (the server refuses the writes as well).
 */
@Component({
  selector: 'app-inventory-items',
  imports: [
    ReactiveFormsModule, ButtonComponent, CardComponent, CategoriesPanelComponent, IconComponent, InputComponent, InventoryTabsComponent, ItemFormModalComponent, ItemHistoryModalComponent,
    PageHeaderComponent, PaginationComponent, SelectComponent, TableComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/inventory-page.scss', '../../styles/inventory-items.scss'],
  templateUrl: './items.component.html',
})
export class InventoryItemsComponent implements OnInit {
  private readonly service = inject(InventoryService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Inventory' }, { label: 'Items / Categories' }];
  protected readonly columns = COLUMNS;
  protected readonly statusOptions = STATUS_OPTIONS;
  protected readonly stockOptions = STOCK_OPTIONS;

  protected readonly view = signal<'items' | 'categories'>('items');
  protected readonly canManage = signal(false);
  protected readonly items = signal<InvItem[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busyId = signal<number | null>(null);

  protected readonly categories = signal<InvCategory[]>([]);
  protected readonly units = signal<InvUnit[]>([]);
  /** null = closed; { item: null } = create; { item } = edit */
  protected readonly form = signal<{ item: InvItem | null } | null>(null);
  protected readonly history = signal<InvItem | null>(null);

  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly category = new FormControl('', { nonNullable: true });
  protected readonly status = new FormControl('active', { nonNullable: true });
  protected readonly stock = new FormControl('', { nonNullable: true });

  protected readonly categoryOptions = computed<SelectOption[]>(() => [{ value: '', label: 'All categories' }, ...this.categories().map((c) => ({ value: String(c.categoryId), label: c.categoryName }))]);

  protected readonly rows = computed<ItemRow[]>(() =>
    this.items().map((i) => {
      const label = stockLabel(i);
      return {
        itemId: i.itemId, photo: i.hasPhoto ? this.service.photoPath(i.itemId) : '', itemCode: i.itemCode, itemName: i.itemName, categoryName: i.categoryName,
        onHand: `${formatQty(i.currentQuantity)} ${i.unitCode}`, minimum: i.minStockLevel > 0 ? `${formatQty(i.minStockLevel)} ${i.unitCode}` : '—',
        price: formatMoney(i.purchasePrice), expiry: formatDate(i.expiryDate), stock: label, statusName: i.isActive ? 'Active' : 'Inactive', isActive: i.isActive,
      };
    }),
  );

  ngOnInit(): void {
    this.service.categories().subscribe({ next: (r) => { this.categories.set(r.items); this.canManage.set(r.canManage); }, error: () => undefined });
    this.service.units().subscribe({ next: (u) => this.units.set(u), error: () => undefined });
    this.load();
    this.search.valueChanges.pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.applyFilters());
    for (const c of [this.category, this.status, this.stock]) c.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.applyFilters());
  }

  private applyFilters(): void {
    this.page.set(1);
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.service
      .items({
        search: this.search.value, categoryId: this.category.value ? Number(this.category.value) : null, status: this.status.value as 'active' | 'inactive' | 'all',
        lowStock: this.stock.value === 'low', expiring: this.stock.value === 'expiring', page: this.page(), pageSize: this.pageSize(),
      })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (p) => { this.items.set(p.items); this.total.set(p.totalCount); this.canManage.set(p.canManage); },
        error: () => this.failed.set(true),
      });
  }

  protected onPage(p: number): void { this.page.set(p); this.load(); }
  protected onPageSize(s: number): void { this.pageSize.set(s); this.applyFilters(); }

  protected itemOf(row: ItemRow): InvItem | null { return this.items().find((i) => i.itemId === row.itemId) ?? null; }
  protected openEdit(row: ItemRow): void { this.form.set({ item: this.itemOf(row) }); }
  protected openHistory(row: ItemRow): void { this.history.set(this.itemOf(row)); }

  protected onSaved(): void {
    this.form.set(null);
    this.load();
  }

  /** A photo was changed in the open form: refresh that row in place without closing the form. */
  protected onPhotoChanged(updated: InvItem): void {
    this.items.update((list) => list.map((i) => (i.itemId === updated.itemId ? updated : i)));
  }

  protected onCategoriesChanged(list: InvCategory[]): void { this.categories.set(list); }

  protected async toggle(row: ItemRow): Promise<void> {
    const deactivating = row.isActive;
    const ok = await this.confirmDialog.confirm({
      title: `${deactivating ? 'Deactivate' : 'Activate'} item?`,
      message: deactivating
        ? `"${row.itemName}" can no longer receive stock or be issued. Its stock (${row.onHand}) and history stay, and you can activate it again later.`
        : `"${row.itemName}" can receive stock and be issued again.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!ok) return;
    this.busyId.set(row.itemId);
    this.service
      .toggleItem(row.itemId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({ next: () => this.load(), error: () => undefined });
  }
}
