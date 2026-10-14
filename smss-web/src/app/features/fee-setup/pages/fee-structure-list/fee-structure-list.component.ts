import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
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
import { FeeStructureRow, FeeStructureSummary } from '../../models/fee-structure.model';
import { FeeStructureService } from '../../services/fee-structure.service';

const COLUMNS: TableColumn<FeeStructureRow>[] = [
  { key: 'structureName', label: 'Structure Name', sortable: true },
  { key: 'className', label: 'Class', sortable: true },
  { key: 'academicYearName', label: 'Academic Year', sortable: true },
  { key: 'lineCount', label: 'Fee Heads', sortable: true, width: '120px' },
  { key: 'statusName', label: 'Status', type: 'status', sortable: true },
];

const byText = (a: unknown, b: unknown) => String(a ?? '').localeCompare(String(b ?? ''), undefined, { sensitivity: 'base', numeric: true });

/** The signed-in school's fee structures (one per class per academic year). Building one happens on its own page. */
@Component({
  selector: 'app-fee-structure-list',
  imports: [ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './fee-structure-list.component.scss',
  templateUrl: './fee-structure-list.component.html',
})
export class FeeStructureListComponent implements OnInit {
  private readonly service = inject(FeeStructureService);
  private readonly router = inject(Router);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'Fee Setup' }, { label: 'Fee Structures' }];

  protected readonly items = signal<FeeStructureSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);

  protected readonly search = signal('');
  protected readonly sortKey = signal<string>('className');
  protected readonly sortDirection = signal<SortDirection>('asc');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  private readonly rows = computed<FeeStructureRow[]>(() =>
    this.items().map((s) => ({
      feeStructureId: s.feeStructureId,
      structureName: s.structureName,
      className: s.className,
      academicYearName: s.academicYearName,
      lineCount: s.lineCount,
      statusName: s.statusName,
      isActive: s.isActive,
    })),
  );

  private readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const key = this.sortKey();
    const dir = this.sortDirection() === 'asc' ? 1 : -1;
    const rows = q
      ? this.rows().filter((r) => [r.structureName, r.className, r.academicYearName, r.statusName].some((v) => String(v).toLowerCase().includes(q)))
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

  protected add(): void {
    void this.router.navigate(['/fee-setup/fee-structures/new']);
  }

  protected edit(row: FeeStructureRow): void {
    void this.router.navigate(['/fee-setup/fee-structures', row.feeStructureId, 'edit']);
  }

  protected async toggleStatus(row: FeeStructureRow): Promise<void> {
    const deactivating = row.isActive;
    const confirmed = await this.confirmDialog.confirm({
      title: `${deactivating ? 'Deactivate' : 'Activate'} fee structure?`,
      message: deactivating
        ? `"${row.structureName}" will be marked Inactive. Nothing is deleted and you can activate it again later.`
        : `"${row.structureName}" will be marked Active again.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!confirmed) return;

    this.busyId.set(row.feeStructureId);
    this.service
      .toggleStatus(row.feeStructureId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (updated) => {
          this.items.update((list) => list.map((i) => (i.feeStructureId === updated.feeStructureId ? { ...i, isActive: updated.isActive, statusName: updated.statusName } : i)));
          this.toast.success(`${row.structureName} is now ${updated.statusName}.`);
        },
        error: () => undefined, // already toasted by the interceptor
      });
  }
}
