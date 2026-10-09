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
import { AcademicYearFormModalComponent } from '../../components/academic-year-form-modal/academic-year-form-modal.component';
import { AcademicYear, AcademicYearRow } from '../../models/academic-year.model';
import { AcademicYearService } from '../../services/academic-year.service';

const COLUMNS: TableColumn<AcademicYearRow>[] = [
  { key: 'yearName', label: 'Year Name', sortable: true },
  { key: 'startDate', label: 'Start Date', sortable: true },
  { key: 'endDate', label: 'End Date', sortable: true },
  { key: 'currentLabel', label: 'Current', sortable: true },
  { key: 'statusName', label: 'Status', type: 'status', sortable: true },
];

/** Sort on the raw ISO date, not the formatted text shown in the cell. */
const SORT_KEY: Record<string, keyof AcademicYear> = { startDate: 'startDate', endDate: 'endDate' };

const formatDate = (iso: string): string => {
  const d = new Date(`${iso.slice(0, 10)}T00:00:00`);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
};

/** The signed-in school's own academic years (the server scopes everything to the token's school). */
@Component({
  selector: 'app-academic-year-list',
  imports: [ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, TableComponent, AcademicYearFormModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './academic-year-list.component.scss',
  templateUrl: './academic-year-list.component.html',
})
export class AcademicYearListComponent implements OnInit {
  private readonly service = inject(AcademicYearService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'School Setup' }, { label: 'Academic Year' }];

  protected readonly years = signal<AcademicYear[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  /** null = closed; { year: null } = create; { year } = edit */
  protected readonly modal = signal<{ year: AcademicYear | null } | null>(null);

  protected readonly search = signal('');
  protected readonly sortKey = signal<string | null>('startDate');
  protected readonly sortDirection = signal<SortDirection>('desc');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  private readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const key = this.sortKey();
    const dir = this.sortDirection() === 'asc' ? 1 : -1;

    const rows = this.years().filter(
      (y) => !q || [y.yearName, formatDate(y.startDate), formatDate(y.endDate), y.statusName ?? '', y.isCurrent ? 'current' : ''].some((t) => t.toLowerCase().includes(q)),
    );
    if (!key) return rows;

    const raw = SORT_KEY[key];
    return [...rows].sort((a, b) => {
      const av = String(raw ? a[raw] : this.toRow(a)[key] ?? '');
      const bv = String(raw ? b[raw] : this.toRow(b)[key] ?? '');
      return av.localeCompare(bv, undefined, { sensitivity: 'base', numeric: true }) * dir;
    });
  });

  protected readonly total = computed(() => this.filtered().length);
  protected readonly pageRows = computed<AcademicYearRow[]>(() => {
    const start = (this.page() - 1) * this.pageSize();
    return this.filtered().slice(start, start + this.pageSize()).map((y) => this.toRow(y));
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
        next: (years) => this.years.set(years),
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  private toRow(y: AcademicYear): AcademicYearRow {
    const isActive = y.statusName === 'Active';
    return {
      academicYearId: y.academicYearId,
      yearName: y.yearName,
      startDate: formatDate(y.startDate),
      endDate: formatDate(y.endDate),
      currentLabel: y.isCurrent ? '★ Current' : '—',
      statusName: y.statusName ?? 'Inactive',
      isCurrent: y.isCurrent,
      isActive,
    };
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
    this.modal.set({ year: null });
  }

  protected openEdit(row: AcademicYearRow): void {
    this.modal.set({ year: this.years().find((y) => y.academicYearId === row.academicYearId) ?? null });
  }

  protected onSaved(): void {
    this.modal.set(null);
    // The first-ever year becomes current server-side; reload so every row's flags stay authoritative
    this.load();
  }

  protected async toggleStatus(row: AcademicYearRow): Promise<void> {
    const deactivating = row.isActive;
    const confirmed = await this.confirmDialog.confirm({
      title: `${deactivating ? 'Deactivate' : 'Activate'} academic year?`,
      message: deactivating
        ? `"${row.yearName}" will be marked Inactive. Nothing is deleted and you can activate it again later.`
        : `"${row.yearName}" will be marked Active again.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!confirmed) return;

    this.run(row.academicYearId, this.service.toggleStatus(row.academicYearId), (y) => `${y.yearName} is now ${y.statusName}.`);
  }

  protected async setCurrent(row: AcademicYearRow): Promise<void> {
    const confirmed = await this.confirmDialog.confirm({
      title: 'Set as current academic year?',
      message: `"${row.yearName}" will become the current academic year for your school. The year that is current now will no longer be current.`,
      confirmText: 'Set as current',
    });
    if (!confirmed) return;

    // Other rows change too (the old current year), so the whole list is refreshed afterwards
    this.run(row.academicYearId, this.service.setCurrent(row.academicYearId), (y) => `${y.yearName} is now the current academic year.`, true);
  }

  private run(id: number, request$: ReturnType<AcademicYearService['toggleStatus']>, message: (y: AcademicYear) => string, reload = false): void {
    this.busyId.set(id);
    request$.pipe(finalize(() => this.busyId.set(null))).subscribe({
      next: (updated) => {
        this.years.update((list) => list.map((y) => (y.academicYearId === updated.academicYearId ? updated : y)));
        this.toast.success(message(updated));
        if (reload) this.load();
      },
      // The server's refusal (e.g. "Set a different year as current…") is already toasted by the interceptor
      error: () => undefined,
    });
  }
}
