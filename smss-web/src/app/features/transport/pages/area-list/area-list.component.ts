import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { debounceTime, finalize } from 'rxjs';
import { AuthService } from '../../../../core/services/auth.service';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import {
  ButtonComponent,
  CardComponent,
  IconComponent,
  InputComponent,
  PageHeaderComponent,
  PaginationComponent,
  SelectComponent,
  TableColumn,
  TableComponent,
} from '../../../../shared/components';
import { AreaFormModalComponent } from '../../components/area-form-modal/area-form-modal.component';
import { STATUS_FILTERS, TRANSPORT_MANAGER_ROLES, TransportArea, formatMoney } from '../../models/transport.model';
import { TransportService } from '../../services/transport.service';

interface AreaRow extends Record<string, unknown> {
  areaId: number;
  areaName: string;
  distance: string;
  fee: string;
  drivers: number;
  remarks: string;
  statusName: string;
  isActive: boolean;
}

const COLUMNS: TableColumn<AreaRow>[] = [
  { key: 'areaName', label: 'Area' },
  { key: 'distance', label: 'Distance' },
  { key: 'fee', label: 'Transport fee' },
  { key: 'drivers', label: 'Active drivers', width: '130px' },
  { key: 'remarks', label: 'Remarks' },
  { key: 'statusName', label: 'Status', type: 'status', width: '110px' },
];

/** The school's transport areas (the places buses cover), filtered and paged on the server. */
@Component({
  selector: 'app-area-list',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, SelectComponent, TableComponent, AreaFormModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../transport-list.scss',
  templateUrl: './area-list.component.html',
})
export class AreaListComponent implements OnInit {
  private readonly service = inject(TransportService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly auth = inject(AuthService);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'Transport' }, { label: 'Areas' }];
  protected readonly statusOptions = STATUS_FILTERS;

  /** The Principal reads; only the School Admin sees the write buttons (the API re-checks). */
  protected readonly canManage = computed(() => this.auth.currentUserRoles().some((r) => (TRANSPORT_MANAGER_ROLES as readonly string[]).includes(r)));

  protected readonly searchControl = new FormControl('', { nonNullable: true });
  protected readonly statusControl = new FormControl('', { nonNullable: true });

  protected readonly items = signal<TransportArea[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  /** null = closed; { area: null } = create; { area } = edit */
  protected readonly modal = signal<{ area: TransportArea | null } | null>(null);
  private readonly filterVersion = signal(0);

  protected readonly rows = computed<AreaRow[]>(() =>
    this.items().map((a) => ({
      areaId: a.areaId,
      areaName: a.areaName,
      distance: a.distanceKm == null ? '' : `${a.distanceKm} km`,
      fee: formatMoney(a.transportFee),
      drivers: a.activeDriverCount,
      remarks: a.remarks ?? '',
      statusName: a.statusName,
      isActive: a.statusName === 'Active',
    })),
  );

  protected readonly hasFilter = computed(() => {
    this.filterVersion();
    return this.searchControl.value !== '' || this.statusControl.value !== '';
  });
  protected readonly emptyMessage = computed(() =>
    this.loadFailed() ? 'Could not load areas.' : this.hasFilter() ? 'No areas match these filters.' : 'No areas yet. Add your first one to get started.',
  );

  ngOnInit(): void {
    this.statusControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.filtersChanged());
    this.searchControl.valueChanges.pipe(debounceTime(300), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.filtersChanged());
    this.load();
  }

  protected filtersChanged(): void {
    this.filterVersion.update((v) => v + 1);
    this.page.set(1);
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .listAreas({ search: this.searchControl.value, status: this.statusControl.value, page: this.page(), pageSize: this.pageSize() })
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (p) => {
          this.items.set(p.items);
          this.total.set(p.total);
        },
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  protected onPage(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected onPageSize(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
    this.load();
  }

  protected clearFilters(): void {
    this.searchControl.setValue('', { emitEvent: false });
    this.statusControl.setValue('', { emitEvent: false });
    this.filtersChanged();
  }

  protected openCreate(): void {
    this.modal.set({ area: null });
  }

  protected openEdit(row: AreaRow): void {
    this.modal.set({ area: this.items().find((a) => a.areaId === row.areaId) ?? null });
  }

  protected onSaved(): void {
    this.modal.set(null);
    this.load(); // server-side paging and filtering: reload rather than patch the page
  }

  protected async toggleStatus(row: AreaRow): Promise<void> {
    const deactivating = row.isActive;
    const confirmed = await this.confirmDialog.confirm({
      title: `${deactivating ? 'Deactivate' : 'Activate'} area?`,
      message: deactivating
        ? `"${row.areaName}" will be marked Inactive and drivers can no longer be assigned to it. Nothing is deleted and you can activate it again later.`
        : `"${row.areaName}" will be marked Active again.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!confirmed) return;

    this.busyId.set(row.areaId);
    this.service
      .toggleArea(row.areaId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (updated) => {
          this.toast.success(`${updated.areaName} is now ${updated.statusName}.`);
          this.load();
        },
        error: () => undefined, // already toasted by the interceptor
      });
  }
}
