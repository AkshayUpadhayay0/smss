import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { catchError, finalize, forkJoin, of } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { MasterDataService } from '../../../../core/services/master-data.service';
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
import { SchoolsListModel } from '../../models/school.model';
import { SchoolService } from '../../services/school.service';

interface SchoolRow extends SchoolsListModel {
  /** Resolved from schoolStatusId via the general-status lookup. */
  statusName: string;
}

@Component({
  selector: 'app-school-list',
  imports: [ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './school-list.component.scss',
  templateUrl: './school-list.component.html',
})
export class SchoolListComponent {
  private readonly schools = inject(SchoolService);
  private readonly master = inject(MasterDataService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  protected readonly columns: TableColumn<SchoolRow>[] = [
    { key: 'schoolCode', label: 'UDISE Code', sortable: true, width: '150px' },
    { key: 'schoolName', label: 'School Name', sortable: true },
    { key: 'email', label: 'Email', sortable: true },
    { key: 'mobileNumber', label: 'Mobile', sortable: true, width: '140px' },
    { key: 'statusName', label: 'Status', sortable: true, type: 'status', width: '120px' },
  ];

  protected readonly rows = signal<SchoolRow[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly togglingId = signal<string | null>(null);

  protected readonly search = signal('');
  protected readonly sortKey = signal<string | null>('schoolCode');
  protected readonly sortDirection = signal<SortDirection>('asc');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  /** statusId -> name, from the general-status lookup (no hardcoded ids). */
  private statusNames = new Map<number, string>();

  private readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const key = this.sortKey() as keyof SchoolRow | null;
    const dir = this.sortDirection() === 'asc' ? 1 : -1;

    const rows = q
      ? this.rows().filter((r) => this.columns.some((c) => String(r[c.key] ?? '').toLowerCase().includes(q)))
      : this.rows();
    if (!key) return rows;

    return [...rows].sort((a, b) => {
      const av = String(a[key] ?? '');
      const bv = String(b[key] ?? '');
      // blanks always last, whatever the direction
      if (!av && bv) return 1;
      if (av && !bv) return -1;
      return av.localeCompare(bv, undefined, { sensitivity: 'base', numeric: true }) * dir;
    });
  });

  protected readonly total = computed(() => this.filtered().length);
  protected readonly pageRows = computed(() => {
    const start = (this.page() - 1) * this.pageSize();
    return this.filtered().slice(start, start + this.pageSize());
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    forkJoin({
      schools: this.schools.getSchools(),
      statuses: this.master.getStatusesByType('general status').pipe(catchError(() => of([]))),
    })
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(),
      )
      .subscribe({
        next: ({ schools, statuses }) => {
          this.statusNames = new Map(statuses.map((s) => [s.sid, s.sname]));
          this.rows.set((schools ?? []).map((s) => this.toRow(s)));
        },
        error: () => this.loadFailed.set(true), // the error interceptor already toasted
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
    void this.router.navigate(['/schools/add']);
  }
  protected view(row: SchoolRow): void {
    void this.router.navigate(['/schools', row.schoolId, 'view']);
  }
  protected edit(row: SchoolRow): void {
    void this.router.navigate(['/schools', row.schoolId, 'edit']);
  }

  protected isActive(row: SchoolRow): boolean {
    return row.statusName.toLowerCase() === 'active';
  }

  protected async toggleStatus(row: SchoolRow): Promise<void> {
    const deactivating = this.isActive(row);
    const confirmed = await this.confirmDialog.confirm({
      title: deactivating ? 'Deactivate school?' : 'Activate school?',
      message: deactivating
        ? `"${row.schoolName}" will be marked Inactive. This does not delete any data and you can activate it again later.`
        : `"${row.schoolName}" will be marked Active again.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!confirmed) return;

    this.togglingId.set(row.schoolId);
    this.schools
      .toggleStatus(row.schoolId)
      .pipe(finalize(() => this.togglingId.set(null)))
      .subscribe({
        next: (updated) => {
          this.rows.update((list) => list.map((r) => (r.schoolId === updated.schoolId ? this.toRow(updated) : r)));
          this.toast.success(`${updated.schoolName} is now ${this.statusNames.get(updated.schoolStatusId ?? -1) ?? 'updated'}.`);
        },
        error: () => undefined, // already toasted by the interceptor
      });
  }

  private toRow(s: SchoolsListModel): SchoolRow {
    return { ...s, statusName: s.schoolStatusId != null ? (this.statusNames.get(s.schoolStatusId) ?? '') : '' };
  }
}
