import { HttpErrorResponse } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Subscription, finalize } from 'rxjs';
import { MasterConfig, MasterItem } from '../../../core/models/master-data.model';
import { MasterAdminService, extractApiError } from '../../../core/services/master-admin.service';
import { MasterFormDialogComponent } from '../master-form-dialog/master-form-dialog.component';

type StatusFilter = 'all' | 'active' | 'inactive';

@Component({
  selector: 'app-master-list',
  standalone: true,
  imports: [CommonModule, FormsModule, MasterFormDialogComponent],
  templateUrl: './master-list.component.html',
  styleUrls: ['./master-list.component.scss']
})
export class MasterListComponent implements OnInit, OnDestroy {
  private readonly cdr = inject(ChangeDetectorRef);

  config!: MasterConfig;
  items: MasterItem[] = [];

  loading = false;
  loadError = '';

  search = '';
  statusFilter: StatusFilter = 'all';
  page = 1;
  readonly pageSize = 10;

  dialogOpen = false;
  editingItem: MasterItem | null = null;
  saving = false;
  dialogError = '';

  confirmItem: MasterItem | null = null;
  toggling = false;

  toast: { text: string; type: 'success' | 'error' } | null = null;
  private toastTimer?: ReturnType<typeof setTimeout>;
  private sub?: Subscription;

  constructor(private route: ActivatedRoute, private api: MasterAdminService) {}

  ngOnInit(): void {
    this.sub = this.route.data.subscribe(d => {
      this.config = d['config'];
      this.items = [];
      this.search = '';
      this.statusFilter = 'all';
      this.page = 1;
      this.load();
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    clearTimeout(this.toastTimer);
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
          this.showToast(res.message, 'success');
          this.load();
        },
        error: (err: HttpErrorResponse) => {
          this.dialogError = extractApiError(err);
        }
      });
  }

  // ---------- activate / deactivate ----------
  askToggle(item: MasterItem): void { this.confirmItem = item; }
  cancelToggle(): void { if (!this.toggling) this.confirmItem = null; }

  confirmToggle(): void {
    if (!this.confirmItem) return;
    this.toggling = true;
    const id = this.confirmItem[this.config.keys.id];

    this.api.toggleStatus(this.config.apiPath, id)
      .pipe(finalize(() => {
        this.toggling = false;
        this.confirmItem = null;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: res => {
          this.showToast(res.message, 'success');
          this.load();
        },
        error: (err: HttpErrorResponse) => this.showToast(extractApiError(err), 'error')
      });
  }

  // ---------- helpers ----------
  trackById = (_: number, item: MasterItem) => item[this.config.keys.id];

  private showToast(text: string, type: 'success' | 'error'): void {
    clearTimeout(this.toastTimer);
    this.toast = { text, type };
    this.cdr.markForCheck();
    this.toastTimer = setTimeout(() => {
      this.toast = null;
      this.cdr.markForCheck();
    }, 4000);
  }
}