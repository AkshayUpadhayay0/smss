import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
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
  SelectComponent,
  SelectOption,
  SortDirection,
  TableComponent,
} from '../../../../shared/components';
import { MasterFormModalComponent } from '../../components/master-form-modal/master-form-modal.component';
import { MasterConfig, MasterItem, MasterRow } from '../../models/master.model';
import { MasterCrudService } from '../../services/master-crud.service';

/**
 * The one list screen behind every master. A route supplies `config` through route data
 * (bound to this input via withComponentInputBinding).
 */
@Component({
  selector: 'app-master-list',
  imports: [ReactiveFormsModule, SelectComponent, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, TableComponent, MasterFormModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './master-list.component.scss',
  templateUrl: './master-list.component.html',
})
export class MasterListComponent {
  private readonly crud = inject(MasterCrudService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);

  readonly config = input.required<MasterConfig>();

  protected readonly items = signal<MasterItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly togglingId = signal<unknown>(null);

  /** null = closed; { item: null } = create; { item } = edit */
  protected readonly modal = signal<{ item: MasterItem | null } | null>(null);

  protected readonly search = signal('');
  /** Value of the optional config.filter dropdown ('' = all). */
  protected readonly filterControl = new FormControl('', { nonNullable: true });
  private readonly filterValue = toSignal(this.filterControl.valueChanges, { initialValue: '' });
  protected readonly sortKey = signal<string | null>(null);
  protected readonly sortDirection = signal<SortDirection>('asc');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  protected readonly breadcrumbs = computed(() => [{ label: 'Masters' }, { label: this.config().title }]);

  protected readonly filterOptions = computed<SelectOption[]>(() => {
    const f = this.config().filter;
    if (!f) return [];
    const values = [...new Set(this.items().map((i) => i[f.key]).filter((v) => v != null && v !== ''))];
    // the select's own empty placeholder option (labelled allLabel) means "all"
    return values.map((v) => ({ label: f.labelOf?.(v) ?? String(v), value: String(v) }));
  });

  private readonly rows = computed<MasterRow[]>(() => {
    const derive = this.config().derive;
    return this.items().map((item) => ({ ...item, ...derive?.(item), statusName: item.isActive ? 'Active' : 'Inactive' }));
  });

  private readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const columns = this.config().columns;
    const key = this.sortKey();
    const dir = this.sortDirection() === 'asc' ? 1 : -1;

    const f = this.config().filter;
    const wanted = this.filterValue();
    const base = f && wanted ? this.rows().filter((r) => String(r[f.key] ?? '') === wanted) : this.rows();

    const rows = q ? base.filter((r) => columns.some((c) => String(r[c.key] ?? '').toLowerCase().includes(q))) : base;
    if (!key) return rows;

    return [...rows].sort((a, b) => {
      const av = String(a[key] ?? '');
      const bv = String(b[key] ?? '');
      if (!av && bv) return 1; // blanks last in both directions
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
    // Reload (and reset UI state) whenever a route hands us a different master.
    effect(() => {
      const config = this.config();
      untracked(() => {
        this.search.set('');
        this.filterControl.setValue('');
        this.page.set(1);
        this.sortKey.set(config.columns.find((c) => c.sortable)?.key ?? null);
        this.sortDirection.set('asc');
        this.modal.set(null);
        this.load();
      });
    });
  }

  protected load(): void {
    const path = this.config().path;
    this.loading.set(true);
    this.loadFailed.set(false);
    this.crud
      .list(path)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (items) => {
          if (path === this.config().path) this.items.set(items);
        },
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

  protected onFilter(): void {
    this.page.set(1);
  }

  protected onPageSize(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
  }

  protected openCreate(): void {
    this.modal.set({ item: null });
  }

  protected openEdit(row: MasterRow): void {
    this.modal.set({ item: this.items().find((i) => this.idOf(i) === this.idOf(row)) ?? row });
  }

  protected onSaved(saved: MasterItem): void {
    this.modal.set(null);
    const id = this.idOf(saved);
    this.items.update((list) => (list.some((i) => this.idOf(i) === id) ? list.map((i) => (this.idOf(i) === id ? saved : i)) : [...list, saved]));
  }

  protected idOf(item: MasterItem): unknown {
    return item[this.config().idKey];
  }

  protected async toggleStatus(row: MasterRow): Promise<void> {
    const cfg = this.config();
    const deactivating = row.isActive;
    const warning = deactivating ? (cfg.deactivateWarning?.(row) ?? null) : null;
    const name = this.displayName(row);

    const confirmed = await this.confirmDialog.confirm({
      title: warning ? `Deactivate ${cfg.singular}? (warning)` : `${deactivating ? 'Deactivate' : 'Activate'} ${cfg.singular}?`,
      message:
        warning ??
        (deactivating
          ? `"${name}" will be marked Inactive. Nothing is deleted and you can activate it again later.`
          : `"${name}" will be marked Active again.`),
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!confirmed) return;

    this.togglingId.set(this.idOf(row));
    this.crud
      .toggleStatus(cfg.path, this.idOf(row) as number)
      .pipe(finalize(() => this.togglingId.set(null)))
      .subscribe({
        next: (updated) => {
          this.items.update((list) => list.map((i) => (this.idOf(i) === this.idOf(updated) ? updated : i)));
          this.toast.success(`${name} is now ${updated.isActive ? 'Active' : 'Inactive'}.`);
        },
        // The server's refusal (e.g. role still has users) is already toasted by the interceptor;
        // swallowing it here stops RxJS rethrowing it as an uncaught error.
        error: () => undefined,
      });
  }

  /** First non-code, non-description text column — the human-readable name. */
  private displayName(row: MasterRow): string {
    const nameKey = this.config().fields.find((f) => f.type === 'text' && !f.createOnly)?.key ?? this.config().fields[0].key;
    return String(row[nameKey] ?? '');
  }
}
