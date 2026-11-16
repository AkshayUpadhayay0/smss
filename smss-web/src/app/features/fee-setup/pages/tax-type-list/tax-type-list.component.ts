import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import {
  ButtonComponent,
  CardComponent,
  IconComponent,
  InputComponent,
  PageHeaderComponent,
  PaginationComponent,
  SortChange,
  SortDirection,
  TableColumn,
  TableComponent,
} from '../../../../shared/components';
import { TaxTypeFormModalComponent } from '../../components/tax-type-form-modal/tax-type-form-modal.component';
import { TaxType, TaxTypeRow, formatRate } from '../../models/tax-type.model';
import { TaxTypeService } from '../../services/tax-type.service';

const COLUMNS: TableColumn<TaxTypeRow>[] = [
  { key: 'taxName', label: 'Tax Type', sortable: true },
  { key: 'taxCode', label: 'Code', sortable: true },
  { key: 'rateLabel', label: 'Rate', sortable: true },
  { key: 'description', label: 'Description' },
  { key: 'statusName', label: 'Status', type: 'status', sortable: true },
];

const byText = (a: unknown, b: unknown) => String(a ?? '').localeCompare(String(b ?? ''), undefined, { sensitivity: 'base', numeric: true });

/** The signed-in school's own tax types (School Admin only; the server scopes everything to the token's school). */
@Component({
  selector: 'app-tax-type-list',
  imports: [ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, TableComponent, TaxTypeFormModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './tax-type-list.component.scss',
  templateUrl: './tax-type-list.component.html',
})
export class TaxTypeListComponent implements OnInit {
  private readonly service = inject(TaxTypeService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'Fee Setup' }, { label: 'Tax Types' }];

  protected readonly items = signal<TaxType[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  /** null = closed; { taxType: null } = create; { taxType } = edit */
  protected readonly modal = signal<{ taxType: TaxType | null } | null>(null);

  protected readonly search = signal('');
  protected readonly sortKey = signal<string>('taxName');
  protected readonly sortDirection = signal<SortDirection>('asc');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  private readonly rows = computed<TaxTypeRow[]>(() =>
    this.items().map((t) => ({
      taxTypeId: t.taxTypeId,
      taxName: t.taxName,
      taxCode: t.taxCode,
      rateLabel: formatRate(t.ratePercent),
      description: t.description ?? '',
      statusName: t.isActive ? 'Active' : 'Inactive',
      isActive: t.isActive,
    })),
  );

  private readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const key = this.sortKey();
    const dir = this.sortDirection() === 'asc' ? 1 : -1;
    const rows = q ? this.rows().filter((r) => [r.taxName, r.taxCode, r.rateLabel, r.description, r.statusName].some((v) => String(v).toLowerCase().includes(q))) : this.rows();
    return [...rows].sort((a, b) => byText(a[key], b[key]) * dir);
  });

  protected readonly total = computed(() => this.filtered().length);
  protected readonly pageRows = computed(() => {
    const start = (this.page() - 1) * this.pageSize();
    return this.filtered().slice(start, start + this.pageSize());
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .list(true)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (items) => this.items.set(items),
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  protected onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
    this.page.set(1);
  }

  protected onSort(change: SortChange): void {
    this.sortKey.set(change.key);
    this.sortDirection.set(change.direction);
    this.page.set(1);
  }

  protected onPageSize(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
  }

  protected openCreate(): void {
    this.modal.set({ taxType: null });
  }

  protected openEdit(row: TaxTypeRow): void {
    this.modal.set({ taxType: this.items().find((t) => t.taxTypeId === row.taxTypeId) ?? null });
  }

  protected onSaved(saved: TaxType): void {
    this.modal.set(null);
    this.items.update((list) => (list.some((t) => t.taxTypeId === saved.taxTypeId) ? list.map((t) => (t.taxTypeId === saved.taxTypeId ? saved : t)) : [...list, saved]));
  }

  protected async toggleStatus(row: TaxTypeRow): Promise<void> {
    const deactivating = row.isActive;
    const confirmed = await this.confirmDialog.confirm({
      title: `${deactivating ? 'Deactivate' : 'Activate'} tax type?`,
      message: deactivating
        ? `"${row.taxName}" can no longer be chosen for new payments. Payments already taken keep it on their receipts, and you can activate it again later.`
        : `"${row.taxName}" can be chosen for new payments again.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!confirmed) return;

    this.busyId.set(row.taxTypeId);
    (deactivating ? this.service.deactivate(row.taxTypeId) : this.service.activate(row.taxTypeId))
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (updated) => {
          this.items.update((list) => list.map((t) => (t.taxTypeId === updated.taxTypeId ? updated : t)));
          this.toast.success(`${updated.taxName} is now ${updated.isActive ? 'Active' : 'Inactive'}.`);
        },
        error: () => undefined, // already toasted by the interceptor
      });
  }
}
