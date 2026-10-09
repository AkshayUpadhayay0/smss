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
import { ClassFormModalComponent } from '../../components/class-form-modal/class-form-modal.component';
import { ClassRow, SchoolClass } from '../../models/class.model';
import { ClassService } from '../../services/class.service';

const COLUMNS: TableColumn<ClassRow>[] = [
  { key: 'className', label: 'Class Name', sortable: true },
  { key: 'classCode', label: 'Class Code', sortable: true },
  { key: 'sequenceOrder', label: 'Sequence Order', sortable: true },
  { key: 'statusName', label: 'Status', type: 'status', sortable: true },
];

/** The signed-in school's own classes (the server scopes everything to the token's school). */
@Component({
  selector: 'app-class-list',
  imports: [ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, TableComponent, ClassFormModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './class-list.component.scss',
  templateUrl: './class-list.component.html',
})
export class ClassListComponent implements OnInit {
  private readonly service = inject(ClassService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'School Setup' }, { label: 'Class' }];

  protected readonly classes = signal<SchoolClass[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  /** null = closed; { schoolClass: null } = create; { schoolClass } = edit */
  protected readonly modal = signal<{ schoolClass: SchoolClass | null } | null>(null);

  protected readonly search = signal('');
  // Display order, not alphabetical: Nursery before Class 1
  protected readonly sortKey = signal<string | null>('sequenceOrder');
  protected readonly sortDirection = signal<SortDirection>('asc');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  private readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const key = this.sortKey();
    const dir = this.sortDirection() === 'asc' ? 1 : -1;

    const rows = this.classes().map((c) => this.toRow(c)).filter(
      (r) => !q || [r.className, r.classCode, String(r.sequenceOrder), r.statusName].some((t) => t.toLowerCase().includes(q)),
    );
    if (!key) return rows;

    return [...rows].sort((a, b) => {
      const av = a[key];
      const bv = b[key];
      // Ties (and the default order) fall back to the class name, matching the server's ordering
      const primary =
        typeof av === 'number' && typeof bv === 'number'
          ? av - bv
          : String(av ?? '').localeCompare(String(bv ?? ''), undefined, { sensitivity: 'base', numeric: true });
      return (primary || a.className.localeCompare(b.className, undefined, { sensitivity: 'base', numeric: true })) * (primary ? dir : 1);
    });
  });

  protected readonly total = computed(() => this.filtered().length);
  protected readonly pageRows = computed<ClassRow[]>(() => {
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
        next: (classes) => this.classes.set(classes),
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  private toRow(c: SchoolClass): ClassRow {
    return {
      classId: c.classId,
      className: c.className,
      classCode: c.classCode ?? '',
      sequenceOrder: c.sequenceOrder,
      statusName: c.statusName ?? 'Inactive',
      isActive: c.statusName === 'Active',
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
    this.modal.set({ schoolClass: null });
  }

  protected openEdit(row: ClassRow): void {
    this.modal.set({ schoolClass: this.classes().find((c) => c.classId === row.classId) ?? null });
  }

  protected onSaved(saved: SchoolClass): void {
    this.modal.set(null);
    this.classes.update((list) => (list.some((c) => c.classId === saved.classId) ? list.map((c) => (c.classId === saved.classId ? saved : c)) : [...list, saved]));
  }

  protected async toggleStatus(row: ClassRow): Promise<void> {
    const deactivating = row.isActive;
    const confirmed = await this.confirmDialog.confirm({
      title: `${deactivating ? 'Deactivate' : 'Activate'} class?`,
      message: deactivating
        ? `"${row.className}" will be marked Inactive. Nothing is deleted and you can activate it again later.`
        : `"${row.className}" will be marked Active again.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!confirmed) return;

    this.busyId.set(row.classId);
    this.service
      .toggleStatus(row.classId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (updated) => {
          this.classes.update((list) => list.map((c) => (c.classId === updated.classId ? updated : c)));
          this.toast.success(`${updated.className} is now ${updated.statusName}.`);
        },
        error: () => undefined, // already toasted by the interceptor
      });
  }
}
