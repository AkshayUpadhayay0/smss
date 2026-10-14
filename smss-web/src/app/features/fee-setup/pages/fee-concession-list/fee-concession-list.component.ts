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
import { FeeConcessionFormModalComponent } from '../../components/fee-concession-form-modal/fee-concession-form-modal.component';
import { FeeConcession, FeeConcessionRow, formatConcessionValue } from '../../models/fee-concession.model';
import { FeeConcessionService } from '../../services/fee-concession.service';

const COLUMNS: TableColumn<FeeConcessionRow>[] = [
  { key: 'concessionName', label: 'Concession', sortable: true },
  { key: 'concessionCode', label: 'Code', sortable: true },
  { key: 'typeLabel', label: 'Type', sortable: true },
  { key: 'valueLabel', label: 'Value', sortable: true },
  { key: 'appliesTo', label: 'Applies To', sortable: true },
  { key: 'statusName', label: 'Status', type: 'status', sortable: true },
];

const byText = (a: unknown, b: unknown) => String(a ?? '').localeCompare(String(b ?? ''), undefined, { sensitivity: 'base', numeric: true });

/** The signed-in school's own fee concessions (the server scopes everything to the token's school). */
@Component({
  selector: 'app-fee-concession-list',
  imports: [ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, TableComponent, FeeConcessionFormModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './fee-concession-list.component.scss',
  templateUrl: './fee-concession-list.component.html',
})
export class FeeConcessionListComponent implements OnInit {
  private readonly service = inject(FeeConcessionService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'Fee Setup' }, { label: 'Fee Concessions' }];

  protected readonly items = signal<FeeConcession[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  /** null = closed; { concession: null } = create; { concession } = edit */
  protected readonly modal = signal<{ concession: FeeConcession | null } | null>(null);

  protected readonly search = signal('');
  protected readonly sortKey = signal<string>('concessionName');
  protected readonly sortDirection = signal<SortDirection>('asc');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  private readonly rows = computed<FeeConcessionRow[]>(() =>
    this.items().map((c) => ({
      concessionId: c.concessionId,
      concessionName: c.concessionName,
      concessionCode: c.concessionCode,
      typeLabel: c.concessionType === 'PERCENTAGE' ? 'Percentage' : 'Fixed',
      valueLabel: formatConcessionValue(c.concessionType, c.value),
      appliesTo: c.applicableFeeTypeName ?? 'All Fee Heads',
      statusName: c.statusName,
      isActive: c.isActive,
    })),
  );

  private readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const key = this.sortKey();
    const dir = this.sortDirection() === 'asc' ? 1 : -1;
    const rows = q
      ? this.rows().filter((r) => [r.concessionName, r.concessionCode, r.typeLabel, r.valueLabel, r.appliesTo, r.statusName].some((v) => String(v).toLowerCase().includes(q)))
      : this.rows();
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
      .list()
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
    this.modal.set({ concession: null });
  }

  protected openEdit(row: FeeConcessionRow): void {
    this.modal.set({ concession: this.items().find((c) => c.concessionId === row.concessionId) ?? null });
  }

  protected onSaved(saved: FeeConcession): void {
    this.modal.set(null);
    this.items.update((list) => (list.some((c) => c.concessionId === saved.concessionId) ? list.map((c) => (c.concessionId === saved.concessionId ? saved : c)) : [...list, saved]));
  }

  protected async toggleStatus(row: FeeConcessionRow): Promise<void> {
    const deactivating = row.isActive;
    const confirmed = await this.confirmDialog.confirm({
      title: `${deactivating ? 'Deactivate' : 'Activate'} fee concession?`,
      message: deactivating
        ? `"${row.concessionName}" will be marked Inactive. Nothing is deleted and you can activate it again later.`
        : `"${row.concessionName}" will be marked Active again.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!confirmed) return;

    this.busyId.set(row.concessionId);
    this.service
      .toggleStatus(row.concessionId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (updated) => {
          this.items.update((list) => list.map((c) => (c.concessionId === updated.concessionId ? updated : c)));
          this.toast.success(`${updated.concessionName} is now ${updated.statusName}.`);
        },
        error: () => undefined, // already toasted by the interceptor
      });
  }
}
