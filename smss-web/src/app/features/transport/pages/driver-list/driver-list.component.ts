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
  SelectOption,
  TableColumn,
  TableComponent,
} from '../../../../shared/components';
import { DriverFormModalComponent } from '../../components/driver-form-modal/driver-form-modal.component';
import { DriverLoginDialogComponent } from '../../components/driver-login-dialog/driver-login-dialog.component';
import { DriverPhotoDialogComponent } from '../../components/driver-photo-dialog/driver-photo-dialog.component';
import { DriverListItem, DriverLookups, STATUS_FILTERS, TRANSPORT_MANAGER_ROLES, expiryLabel, expiryTone, formatDate } from '../../models/transport.model';
import { TransportService } from '../../services/transport.service';

interface DriverRow extends Record<string, unknown> {
  driverId: string;
  photoUrl: string;
  fullName: string;
  mobileNumber: string;
  dlNumber: string;
  dlValidUpto: string;
  dlBadge: string;
  vehicle: string;
  areas: string;
  statusName: string;
  login: string;
  isActive: boolean;
  hasLogin: boolean;
}

const COLUMNS: TableColumn<DriverRow>[] = [
  { key: 'photoUrl', label: '', type: 'avatar', width: '64px', avatarLabel: (r) => r.fullName },
  { key: 'driverId', label: 'Driver ID' },
  { key: 'fullName', label: 'Name' },
  { key: 'mobileNumber', label: 'Mobile' },
  { key: 'dlNumber', label: 'Licence' },
  { key: 'dlValidUpto', label: 'Valid upto' },
  { key: 'dlBadge', label: 'Licence status', type: 'badge', tone: expiryTone },
  { key: 'vehicle', label: 'Vehicle' },
  { key: 'areas', label: 'Areas' },
  { key: 'statusName', label: 'Status', type: 'status', width: '110px' },
  { key: 'login', label: 'Login', width: '80px' },
];

/** The school's drivers with licence expiry badges, filtered and paged on the server. */
@Component({
  selector: 'app-driver-list',
  imports: [
    ReactiveFormsModule,
    ButtonComponent,
    CardComponent,
    IconComponent,
    InputComponent,
    PageHeaderComponent,
    PaginationComponent,
    SelectComponent,
    TableComponent,
    DriverFormModalComponent,
    DriverPhotoDialogComponent,
    DriverLoginDialogComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../transport-list.scss',
  templateUrl: './driver-list.component.html',
})
export class DriverListComponent implements OnInit {
  private readonly service = inject(TransportService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly auth = inject(AuthService);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'Transport' }, { label: 'Drivers' }];
  protected readonly statusOptions = STATUS_FILTERS;

  protected readonly canManage = computed(() => this.auth.currentUserRoles().some((r) => (TRANSPORT_MANAGER_ROLES as readonly string[]).includes(r)));

  protected readonly searchControl = new FormControl('', { nonNullable: true });
  protected readonly statusControl = new FormControl('', { nonNullable: true });
  protected readonly vehicleControl = new FormControl('', { nonNullable: true });

  protected readonly lookups = signal<DriverLookups | null>(null);
  protected readonly vehicleOptions = computed<SelectOption[]>(() => [{ label: 'All', value: '' }, ...(this.lookups()?.vehicles ?? []).map((v) => ({ label: v.name, value: String(v.id) }))]);

  protected readonly items = signal<DriverListItem[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<string | null>(null);

  /** null = closed; { driverId: null } = create; { driverId } = edit */
  protected readonly modal = signal<{ driverId: string | null } | null>(null);
  protected readonly photoFor = signal<DriverListItem | null>(null);
  protected readonly loginFor = signal<DriverListItem | null>(null);
  private readonly filterVersion = signal(0);

  protected readonly rows = computed<DriverRow[]>(() =>
    this.items().map((d) => ({
      driverId: d.driverId,
      photoUrl: d.photoUrl ?? '',
      fullName: d.fullName,
      mobileNumber: d.mobileNumber,
      dlNumber: d.dlNumber,
      dlValidUpto: formatDate(d.dlValidUpto),
      dlBadge: expiryLabel(d.dlStatus, d.dlDaysLeft),
      vehicle: d.vehicleLabel ? `${d.vehicleLabel}${d.vehiclePlate ? ` (${d.vehiclePlate})` : ''}` : '',
      areas: d.areaNames.join(', '),
      statusName: d.statusName,
      login: d.hasLogin ? 'Yes' : 'No',
      isActive: d.statusName === 'Active',
      hasLogin: d.hasLogin,
    })),
  );

  protected readonly hasFilter = computed(() => {
    this.filterVersion();
    return [this.searchControl, this.statusControl, this.vehicleControl].some((c) => c.value !== '');
  });
  protected readonly emptyMessage = computed(() =>
    this.loadFailed() ? 'Could not load drivers.' : this.hasFilter() ? 'No drivers match these filters.' : 'No drivers yet. Add your first one to get started.',
  );

  ngOnInit(): void {
    this.loadLookups();
    for (const c of [this.statusControl, this.vehicleControl]) c.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.filtersChanged());
    this.searchControl.valueChanges.pipe(debounceTime(300), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.filtersChanged());
    this.load();
  }

  private loadLookups(): void {
    this.service.driverLookups().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (l) => this.lookups.set(l), error: () => undefined });
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
      .listDrivers({ search: this.searchControl.value, status: this.statusControl.value, page: this.page(), pageSize: this.pageSize() }, this.vehicleControl.value)
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
    for (const c of [this.searchControl, this.statusControl, this.vehicleControl]) c.setValue('', { emitEvent: false });
    this.filtersChanged();
  }

  protected openForm(row: DriverRow | null): void {
    if (!this.lookups()) {
      this.toast.warning('Vehicles and areas are still loading. Please try again in a moment.');
      return;
    }
    this.modal.set({ driverId: row?.driverId ?? null });
  }

  protected onSaved(): void {
    this.modal.set(null);
    this.loadLookups(); // an assigned vehicle / area may have changed
    this.load(); // server-side paging and filtering: reload rather than patch the page
  }

  protected itemOf(row: DriverRow): DriverListItem | null {
    return this.items().find((d) => d.driverId === row.driverId) ?? null;
  }

  protected onPhotoSaved(): void {
    this.photoFor.set(null);
    this.load();
  }

  protected onLoginCreated(): void {
    this.loginFor.set(null);
    this.load();
  }

  protected async toggleStatus(row: DriverRow): Promise<void> {
    const deactivating = row.isActive;
    const confirmed = await this.confirmDialog.confirm({
      title: `${deactivating ? 'Deactivate' : 'Activate'} driver?`,
      message: deactivating
        ? `"${row.fullName}" will be marked Inactive${row.hasLogin ? ', and their login stops working straight away (they are signed out)' : ''}. Nothing is deleted and you can activate them again later.`
        : `"${row.fullName}" will be marked Active again${row.hasLogin ? ' and their login works again' : ''}. This is refused while their vehicle or an area is inactive.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!confirmed) return;

    this.busyId.set(row.driverId);
    this.service
      .toggleDriver(row.driverId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (updated) => {
          this.toast.success(`${updated.fullName} is now ${updated.statusName}.`);
          this.load();
        },
        error: () => undefined, // already toasted by the interceptor (e.g. reactivation blocked by an inactive vehicle / area)
      });
  }
}
