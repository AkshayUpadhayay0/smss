import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, TableColumn, TableComponent } from '../../../../shared/components';
import { InventoryTabsComponent } from '../../components/inventory-tabs/inventory-tabs.component';
import { SupplierFormModalComponent } from '../../components/supplier-form-modal/supplier-form-modal.component';
import { InvSupplier } from '../../models/inventory.model';
import { InventoryService } from '../../services/inventory.service';

interface SupplierRow extends Record<string, unknown> { supplierId: number; supplierName: string; contact: string; mobile: string; email: string; gstin: string; statusName: string; isActive: boolean }

const COLUMNS: TableColumn<SupplierRow>[] = [
  { key: 'supplierName', label: 'Supplier' },
  { key: 'contact', label: 'Contact person' },
  { key: 'mobile', label: 'Mobile' },
  { key: 'email', label: 'Email' },
  { key: 'gstin', label: 'GSTIN' },
  { key: 'statusName', label: 'Status', type: 'status' },
];

/** The school's suppliers (a small list, searched in the browser). Add / edit / (de)activate only when the server says the caller can manage. */
@Component({
  selector: 'app-inventory-suppliers',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, InventoryTabsComponent, PageHeaderComponent, PaginationComponent, SupplierFormModalComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../../styles/inventory-page.scss',
  templateUrl: './suppliers.component.html',
})
export class InventorySuppliersComponent implements OnInit {
  private readonly service = inject(InventoryService);
  private readonly confirmDialog = inject(ConfirmDialogService);

  protected readonly breadcrumbs = [{ label: 'Inventory' }, { label: 'Suppliers' }];
  protected readonly columns = COLUMNS;

  protected readonly canManage = signal(false);
  protected readonly suppliers = signal<InvSupplier[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  protected readonly modal = signal<{ supplier: InvSupplier | null } | null>(null);
  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly searchText = signal('');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  private readonly filtered = computed(() => {
    const q = this.searchText().trim().toLowerCase();
    const rows = this.suppliers().map<SupplierRow>((s) => ({
      supplierId: s.supplierId, supplierName: s.supplierName, contact: s.contactPerson ?? '—', mobile: s.mobile ?? '—', email: s.email ?? '—', gstin: s.gstin ?? '—',
      statusName: s.isActive ? 'Active' : 'Inactive', isActive: s.isActive,
    }));
    return q ? rows.filter((r) => [r.supplierName, r.contact, r.mobile, r.email, r.gstin].some((v) => String(v).toLowerCase().includes(q))) : rows;
  });
  protected readonly total = computed(() => this.filtered().length);
  protected readonly pageRows = computed(() => this.filtered().slice((this.page() - 1) * this.pageSize(), this.page() * this.pageSize()));

  ngOnInit(): void {
    this.load();
    this.search.valueChanges.subscribe((v) => { this.searchText.set(v); this.page.set(1); });
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.service
      .suppliers(true)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({ next: (r) => { this.suppliers.set(r.items); this.canManage.set(r.canManage); }, error: () => this.failed.set(true) });
  }

  protected edit(row: SupplierRow): void {
    this.modal.set({ supplier: this.suppliers().find((s) => s.supplierId === row.supplierId) ?? null });
  }

  protected onSaved(): void {
    this.modal.set(null);
    this.load();
  }

  protected async toggle(row: SupplierRow): Promise<void> {
    const deactivating = row.isActive;
    const ok = await this.confirmDialog.confirm({
      title: `${deactivating ? 'Deactivate' : 'Activate'} supplier?`,
      message: deactivating
        ? `"${row.supplierName}" can no longer be chosen when adding stock. Past stock-ins keep their supplier, and you can activate it again later.`
        : `"${row.supplierName}" can be chosen when adding stock again.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!ok) return;
    this.busyId.set(row.supplierId);
    this.service
      .toggleSupplier(row.supplierId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({ next: () => this.load(), error: () => undefined });
  }
}
