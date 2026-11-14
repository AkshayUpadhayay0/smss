import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Subscription, debounceTime, finalize } from 'rxjs';
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
  SelectOption,
  TableColumn,
  TableComponent,
} from '../../../../shared/components';
import { VehicleFormModalComponent } from '../../components/vehicle-form-modal/vehicle-form-modal.component';
import { STATUS_FILTERS, TRANSPORT_MANAGER_ROLES, Vehicle, VehicleLookups, expiryLabel, expiryTone } from '../../models/transport.model';
import { TransportService } from '../../services/transport.service';

interface VehicleRow extends Record<string, unknown> {
  vehicleId: number;
  vehicleLabel: string;
  registrationNumber: string;
  vehicleTypeName: string;
  ownershipName: string;
  seatingCapacity: string;
  puc: string;
  insurance: string;
  drivers: number;
  statusName: string;
  isActive: boolean;
}

const COLUMNS: TableColumn<VehicleRow>[] = [
  { key: 'vehicleLabel', label: 'Vehicle' },
  { key: 'registrationNumber', label: 'Registration' },
  { key: 'vehicleTypeName', label: 'Type' },
  { key: 'ownershipName', label: 'Ownership' },
  { key: 'seatingCapacity', label: 'Seats', width: '80px' },
  { key: 'puc', label: 'PUC', type: 'badge', tone: expiryTone },
  { key: 'insurance', label: 'Insurance', type: 'badge', tone: expiryTone },
  { key: 'drivers', label: 'Active drivers', width: '120px' },
  { key: 'statusName', label: 'Status', type: 'status', width: '110px' },
];

/** The school's vehicles with PUC and insurance expiry badges, filtered and paged on the server. */
@Component({
  selector: 'app-vehicle-list',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, SelectComponent, TableComponent, VehicleFormModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../transport-list.scss',
  templateUrl: './vehicle-list.component.html',
})
export class VehicleListComponent implements OnInit {
  private readonly service = inject(TransportService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly auth = inject(AuthService);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'Transport' }, { label: 'Vehicles' }];
  protected readonly statusOptions = STATUS_FILTERS;

  protected readonly canManage = computed(() => this.auth.currentUserRoles().some((r) => (TRANSPORT_MANAGER_ROLES as readonly string[]).includes(r)));

  protected readonly searchControl = new FormControl('', { nonNullable: true });
  protected readonly statusControl = new FormControl('', { nonNullable: true });
  protected readonly typeControl = new FormControl('', { nonNullable: true });

  protected readonly lookups = signal<VehicleLookups | null>(null);
  protected readonly typeOptions = computed<SelectOption[]>(() => [{ label: 'All', value: '' }, ...(this.lookups()?.vehicleTypes ?? []).map((t) => ({ label: t.name, value: String(t.id) }))]);

  protected readonly items = signal<Vehicle[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  /** null = closed; { vehicle: null } = create; { vehicle } = edit */
  protected readonly modal = signal<{ vehicle: Vehicle | null } | null>(null);
  private readonly filterVersion = signal(0);

  protected readonly rows = computed<VehicleRow[]>(() =>
    this.items().map((v) => ({
      vehicleId: v.vehicleId,
      vehicleLabel: v.vehicleLabel,
      registrationNumber: v.registrationNumber,
      vehicleTypeName: v.vehicleTypeName,
      ownershipName: v.ownershipName,
      seatingCapacity: v.seatingCapacity == null ? '' : String(v.seatingCapacity),
      puc: expiryLabel(v.pucStatus, v.pucDaysLeft),
      insurance: expiryLabel(v.insuranceStatus, v.insuranceDaysLeft),
      drivers: v.activeDriverCount,
      statusName: v.statusName,
      isActive: v.statusName === 'Active',
    })),
  );

  protected readonly hasFilter = computed(() => {
    this.filterVersion();
    return [this.searchControl, this.statusControl, this.typeControl].some((c) => c.value !== '');
  });
  protected readonly emptyMessage = computed(() =>
    this.loadFailed() ? 'Could not load vehicles.' : this.hasFilter() ? 'No vehicles match these filters.' : 'No vehicles yet. Add your first one to get started.',
  );

  ngOnInit(): void {
    this.service.vehicleLookups().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (l) => this.lookups.set(l), error: () => undefined });
    for (const c of [this.statusControl, this.typeControl]) c.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.filtersChanged());
    this.searchControl.valueChanges.pipe(debounceTime(300), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.filtersChanged());
    this.load();
  }

  protected filtersChanged(): void {
    this.filterVersion.update((v) => v + 1);
    this.page.set(1);
    this.load();
  }

  /** The request on screen: a new search / filter / page cancels it, so a slower OLDER answer can never overwrite the newer one. */
  private request?: Subscription;

  protected load(): void {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.loadFailed.set(false);
    this.request = this.service
      .listVehicles({ search: this.searchControl.value, status: this.statusControl.value, page: this.page(), pageSize: this.pageSize() }, this.typeControl.value)
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
    for (const c of [this.searchControl, this.statusControl, this.typeControl]) c.setValue('', { emitEvent: false });
    this.filtersChanged();
  }

  protected openCreate(): void {
    if (this.lookups()) this.modal.set({ vehicle: null });
    else this.toast.warning('Vehicle types are still loading. Please try again in a moment.');
  }

  protected openEdit(row: VehicleRow): void {
    if (!this.lookups()) {
      this.toast.warning('Vehicle types are still loading. Please try again in a moment.');
      return;
    }
    this.modal.set({ vehicle: this.items().find((v) => v.vehicleId === row.vehicleId) ?? null });
  }

  protected onSaved(): void {
    this.modal.set(null);
    this.load(); // server-side paging and filtering: reload rather than patch the page
  }

  protected async toggleStatus(row: VehicleRow): Promise<void> {
    const deactivating = row.isActive;
    const confirmed = await this.confirmDialog.confirm({
      title: `${deactivating ? 'Deactivate' : 'Activate'} vehicle?`,
      message: deactivating
        ? `"${row.vehicleLabel}" will be marked Inactive and drivers can no longer be assigned to it. Nothing is deleted and you can activate it again later.`
        : `"${row.vehicleLabel}" will be marked Active again.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!confirmed) return;

    this.busyId.set(row.vehicleId);
    this.service
      .toggleVehicle(row.vehicleId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (updated) => {
          this.toast.success(`${updated.vehicleLabel} is now ${updated.statusName}.`);
          this.load();
        },
        error: () => undefined, // already toasted by the interceptor
      });
  }
}
