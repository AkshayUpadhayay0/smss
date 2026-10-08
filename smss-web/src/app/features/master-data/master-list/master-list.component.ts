import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Subscription, finalize } from 'rxjs';
import { TableColumn } from '../../../core/models';
import { MasterConfig, MasterItem } from '../../../core/models/master-data.model';
import { MasterAdminService, extractApiError } from '../../../core/services/master-admin.service';
import { ConfirmDialogService } from '../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../core/services/toast.service';
import { BadgeComponent } from '../../../shared/components/badge/badge.component';
import { ButtonComponent } from '../../../shared/components/button/button.component';
import { CardComponent } from '../../../shared/components/card/card.component';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/components/page-header/page-header.component';
import { PaginationComponent } from '../../../shared/components/pagination/pagination.component';
import { TableCellDirective } from '../../../shared/components/table/table-cell.directive';
import { TableComponent } from '../../../shared/components/table/table.component';
import { MasterFormDialogComponent } from '../master-form-dialog/master-form-dialog.component';

type StatusFilter = 'all' | 'active' | 'inactive';

@Component({
  selector: 'app-master-list',
  standalone: true,
  imports: [
    FormsModule, MasterFormDialogComponent, PageHeaderComponent, CardComponent, ButtonComponent, IconComponent,
    BadgeComponent, EmptyStateComponent, PaginationComponent, TableComponent, TableCellDirective
  ],
  templateUrl: './master-list.component.html',
  styleUrls: ['./master-list.component.scss']
})
export class MasterListComponent implements OnInit, OnDestroy {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly toastService = inject(ToastService);
  private readonly confirmDialogService = inject(ConfirmDialogService);

  config!: MasterConfig;
  columns: TableColumn<MasterItem>[] = [];
  items: MasterItem[] = [];

  loading = false;
  loadError = '';

  search = '';
  statusFilter: StatusFilter = 'all';
  page = 1;
  pageSize = 10;

  dialogOpen = false;
  editingItem: MasterItem | null = null;
  saving = false;
  dialogError = '';

  /** Id of the row whose status toggle is in flight (blocks double clicks). */
  togglingId: unknown = null;

  private sub?: Subscription;

  constructor(private route: ActivatedRoute, private api: MasterAdminService) {}

  ngOnInit(): void {
    this.sub = this.route.data.subscribe(d => {
      this.config = d['config'];
      this.columns = this.buildColumns(this.config);
      this.items = [];
      this.search = '';
      this.statusFilter = 'all';
      this.page = 1;
      this.load();
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  // ---------- data ----------
  load(): void {
    this.loading = true;
    this.loadError = '';
    this.api.getAll(this.config.apiPath, true)
      .pipe(finalize(() => { this.loading = false; this.cdr.markForCheck(); }))
      .subscribe({
        next: res => { this.items = res.data ?? []; },
        error: (err: HttpErrorResponse) => {
          if (err.status === 404) { this.items = []; return; }   // empty table
          this.loadError = extractApiError(err);
        }
      });
  }

  // ---------- filtering / paging ----------
  get filtered(): MasterItem[] {
  const q = this.search.trim().toLowerCase();
  const k = this.config.keys;
  return this.items.filter(i => {
    if (this.statusFilter === 'active' && !i.isActive) return false;
    if (this.statusFilter === 'inactive' && i.isActive) return false;
    if (!q) return true;
    const haystack = [
      k.code ? i[k.code] : '',
      i[k.name],
      this.config.typeField ? i[this.config.typeField.key] : ''
    ].join(' ').toLowerCase();
    return haystack.includes(q);
  });
}

  get paged(): MasterItem[] {
    const start = (this.page - 1) * this.pageSize;
    return this.filtered.slice(start, start + this.pageSize);
  }

  get totalPages(): number { return Math.max(1, Math.ceil(this.filtered.length / this.pageSize)); }

  onFilterChange(): void { this.page = 1; }
  goTo(p: number): void { this.page = Math.min(Math.max(1, p), this.totalPages); }
  onPageSizeChange(size: number): void { this.pageSize = size; this.page = 1; }

  // ---------- add / edit ----------
  openAdd(): void { this.editingItem = null; this.dialogError = ''; this.dialogOpen = true; }
  openEdit(item: MasterItem): void { this.editingItem = item; this.dialogError = ''; this.dialogOpen = true; }
  closeDialog(): void { if (!this.saving) this.dialogOpen = false; }

  onSave(payload: Record<string, unknown>): void {
    this.saving = true;
    this.dialogError = '';

    const request$ = this.editingItem
      ? this.api.update(this.config.apiPath, this.editingItem[this.config.keys.id], payload)
      : this.api.create(this.config.apiPath, payload);

    request$
      .pipe(finalize(() => { this.saving = false; this.cdr.markForCheck(); }))
      .subscribe({
        next: res => {
          this.dialogOpen = false;
          this.toastService.success(res.message);
          this.load();
        },
        error: (err: HttpErrorResponse) => {
          this.dialogError = extractApiError(err);
        }
      });
  }

  // ---------- activate / deactivate ----------
  async toggleStatus(item: MasterItem): Promise<void> {
    if (this.togglingId !== null) return;
    const willDeactivate = item.isActive;
    const name = item[this.config.keys.name];
    const confirmed = await this.confirmDialogService.confirm({
      title: `${willDeactivate ? 'Deactivate' : 'Activate'} ${this.config.singular}?`,
      message: willDeactivate
        ? `${name} will no longer appear in dropdowns for new records. Existing records that use it are not affected.`
        : `${name} will be available in dropdowns again.`,
      confirmLabel: willDeactivate ? 'Deactivate' : 'Activate',
      variant: willDeactivate ? 'danger' : 'primary',
    });
    if (!confirmed) return;

    const id = item[this.config.keys.id];
    this.togglingId = id;
    this.cdr.markForCheck();

    this.api.toggleStatus(this.config.apiPath, id)
      .pipe(finalize(() => { this.togglingId = null; this.cdr.markForCheck(); }))
      .subscribe({
        next: res => {
          this.toastService.success(res.message);
          this.load();
        },
        error: (err: HttpErrorResponse) => this.toastService.danger('Action failed', extractApiError(err))
      });
  }

  // ---------- helpers ----------
  private buildColumns(config: MasterConfig): TableColumn<MasterItem>[] {
    const cols: TableColumn<MasterItem>[] = [];
    if (config.keys.code) cols.push({ key: config.keys.code, label: config.codeLabel ?? 'Code', width: '140px' });
    cols.push({ key: config.keys.name, label: config.nameLabel });
    if (config.typeField) cols.push({ key: config.typeField.key, label: config.typeField.label });
    if (config.hasDescription !== false) cols.push({ key: 'description', label: 'Description', cellClass: 'hide-mobile' });
    cols.push({ key: 'isActive', label: 'Status', width: '110px' });
    return cols;
  }
}