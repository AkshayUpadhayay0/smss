import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
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
import { SchoolLookupFormModalComponent } from '../../components/school-lookup-form-modal/school-lookup-form-modal.component';
import { SchoolLookupConfig, SchoolLookupItem, SchoolLookupRow } from '../../models/school-lookup.model';
import { SchoolLookupService } from '../../services/school-lookup.service';

const byText = (a: unknown, b: unknown) => String(a ?? '').localeCompare(String(b ?? ''), undefined, { sensitivity: 'base', numeric: true });

/**
 * One list screen behind every simple school-owned master. A route supplies `config` through route data
 * (bound to this input via withComponentInputBinding). The server scopes every call to the token's school.
 */
@Component({
  selector: 'app-school-lookup-list',
  imports: [ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, TableComponent, SchoolLookupFormModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './school-lookup-list.component.scss',
  templateUrl: './school-lookup-list.component.html',
})
export class SchoolLookupListComponent {
  private readonly service = inject(SchoolLookupService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);

  readonly config = input.required<SchoolLookupConfig>();

  protected readonly items = signal<SchoolLookupItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<unknown>(null);
  /** null = closed; { item: null } = create; { item } = edit */
  protected readonly modal = signal<{ item: SchoolLookupItem | null } | null>(null);

  protected readonly search = signal('');
  protected readonly sortKey = signal<string | null>(null);
  protected readonly sortDirection = signal<SortDirection>('asc');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  protected readonly breadcrumbs = computed(() => [{ label: 'School Setup' }, { label: this.config().title }]);

  protected readonly columns = computed<TableColumn<SchoolLookupRow>[]>(() => {
    const c = this.config();
    return [
      { key: c.nameKey, label: 'Name', sortable: true },
      ...(c.codeKey ? [{ key: c.codeKey, label: 'Code', sortable: true }] : []),
      { key: 'description', label: 'Description', sortable: true },
      { key: 'statusName', label: 'Status', type: 'status' as const, sortable: true },
    ];
  });

  private readonly rows = computed<SchoolLookupRow[]>(() =>
    this.items().map((i) => ({ ...i, description: i.description ?? '', statusName: i.statusName ?? 'Inactive', isActive: i.statusName === 'Active' })),
  );

  private readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const cfg = this.config();
    const key = this.sortKey() ?? cfg.nameKey;
    const dir = this.sortDirection() === 'asc' ? 1 : -1;
    const searchKeys = [cfg.nameKey, ...(cfg.codeKey ? [cfg.codeKey] : []), 'description', 'statusName'];

    const rows = q ? this.rows().filter((r) => searchKeys.some((k) => String(r[k] ?? '').toLowerCase().includes(q))) : this.rows();
    return [...rows].sort((a, b) => {
      const av = String(a[key] ?? '');
      const bv = String(b[key] ?? '');
      if (!av && bv) return 1; // blanks last in both directions
      if (av && !bv) return -1;
      return byText(av, bv) * dir;
    });
  });

  protected readonly total = computed(() => this.filtered().length);
  protected readonly pageRows = computed(() => {
    const start = (this.page() - 1) * this.pageSize();
    return this.filtered().slice(start, start + this.pageSize());
  });

  constructor() {
    // Reload (and reset UI state) whenever a route hands us a different master.
    effect(() => {
      const config = this.config();
      untracked(() => {
        this.search.set('');
        this.page.set(1);
        this.sortKey.set(config.nameKey);
        this.sortDirection.set('asc');
        this.modal.set(null);
        this.items.set([]);
        this.load();
      });
    });
  }

  protected load(): void {
    const path = this.config().path;
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .list(path)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (items) => {
          if (path === this.config().path) this.items.set(items);
        },
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  protected idOf(row: SchoolLookupRow | SchoolLookupItem): unknown {
    return row[this.config().idKey];
  }

  private nameOf(row: SchoolLookupRow): string {
    return String(row[this.config().nameKey] ?? '');
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
    this.modal.set({ item: null });
  }

  protected openEdit(row: SchoolLookupRow): void {
    this.modal.set({ item: this.items().find((i) => this.idOf(i) === this.idOf(row)) ?? null });
  }

  protected onSaved(saved: SchoolLookupItem): void {
    this.modal.set(null);
    const id = this.idOf(saved);
    this.items.update((list) => (list.some((i) => this.idOf(i) === id) ? list.map((i) => (this.idOf(i) === id ? saved : i)) : [...list, saved]));
  }

  protected async toggleStatus(row: SchoolLookupRow): Promise<void> {
    const cfg = this.config();
    const deactivating = row.isActive;
    const name = this.nameOf(row);
    const confirmed = await this.confirmDialog.confirm({
      title: `${deactivating ? 'Deactivate' : 'Activate'} ${cfg.singular}?`,
      message: deactivating
        ? `"${name}" will be marked Inactive. Nothing is deleted and you can activate it again later.`
        : `"${name}" will be marked Active again.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!confirmed) return;

    this.busyId.set(this.idOf(row));
    this.service
      .toggleStatus(cfg.path, this.idOf(row) as number)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (updated) => {
          this.items.update((list) => list.map((i) => (this.idOf(i) === this.idOf(updated) ? updated : i)));
          this.toast.success(`${name} is now ${updated.statusName}.`);
        },
        error: () => undefined, // already toasted by the interceptor
      });
  }
}
