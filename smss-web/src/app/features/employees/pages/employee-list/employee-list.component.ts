import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { debounceTime, finalize } from 'rxjs';
import { toUploadUrl } from '../../../../core/utils/upload-url';
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
import { EMPLOYEE_TYPES, EmployeeListItem, EmployeeLookups, EmployeeRow, employmentStatusTone } from '../../models/employee.model';
import { EmployeeService } from '../../services/employee.service';

const COLUMNS: TableColumn<EmployeeRow>[] = [
  { key: 'photoUrl', label: '', type: 'avatar', width: '64px', avatarLabel: (r) => r.fullName },
  { key: 'employeeId', label: 'Employee ID' },
  { key: 'fullName', label: 'Name' },
  { key: 'designationName', label: 'Designation' },
  { key: 'departmentName', label: 'Department' },
  { key: 'employeeType', label: 'Type' },
  { key: 'mobileNumber', label: 'Mobile' },
  { key: 'employmentStatusName', label: 'Employment', type: 'badge', tone: employmentStatusTone },
  { key: 'statusName', label: 'Status', type: 'status', width: '110px' },
  { key: 'login', label: 'Login', width: '80px' },
];

const ALL: SelectOption = { label: 'All', value: '' };
const options = (items: { id: number; name: string }[] | undefined): SelectOption[] => [ALL, ...(items ?? []).map((i) => ({ label: i.name, value: String(i.id) }))];

/** The school's staff, filtered and paged on the server. */
@Component({
  selector: 'app-employee-list',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, SelectComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './employee-list.component.scss',
  templateUrl: './employee-list.component.html',
})
export class EmployeeListComponent implements OnInit {
  private readonly service = inject(EmployeeService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'Employees' }];

  protected readonly searchControl = new FormControl('', { nonNullable: true });
  protected readonly designationControl = new FormControl('', { nonNullable: true });
  protected readonly departmentControl = new FormControl('', { nonNullable: true });
  protected readonly employmentStatusControl = new FormControl('', { nonNullable: true });
  protected readonly typeControl = new FormControl('', { nonNullable: true });
  protected readonly statusControl = new FormControl('', { nonNullable: true });

  protected readonly lookups = signal<EmployeeLookups | null>(null);
  protected readonly designationOptions = computed(() => options(this.lookups()?.designations));
  protected readonly departmentOptions = computed(() => options(this.lookups()?.departments));
  protected readonly employmentStatusOptions = computed(() => options(this.lookups()?.employmentStatuses));
  protected readonly typeOptions: SelectOption[] = [ALL, ...EMPLOYEE_TYPES.map((t) => ({ label: t, value: t }))];
  protected readonly statusOptions: SelectOption[] = [ALL, { label: 'Active', value: 'Active' }, { label: 'Inactive', value: 'Inactive' }];

  protected readonly items = signal<EmployeeListItem[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  protected readonly rows = computed<EmployeeRow[]>(() =>
    this.items().map((e) => ({
      employeeId: e.employeeId,
      photoUrl: toUploadUrl(e.photoUrl) ?? '',
      fullName: e.fullName,
      designationName: e.designationName,
      departmentName: e.departmentName,
      employeeType: e.employeeType,
      mobileNumber: e.mobileNumber,
      employmentStatusName: e.employmentStatusName,
      statusName: e.statusName,
      login: e.hasLogin ? 'Yes' : 'No',
    })),
  );

  protected readonly hasFilter = computed(() => this.activeFilters() > 0);
  private readonly filterVersion = signal(0);
  private activeFilters(): number {
    this.filterVersion();
    return [this.designationControl, this.departmentControl, this.employmentStatusControl, this.typeControl, this.statusControl, this.searchControl].filter((c) => c.value !== '').length;
  }

  protected readonly emptyMessage = computed(() =>
    this.loadFailed() ? 'Could not load employees.' : this.hasFilter() ? 'No employees match these filters.' : 'No employees yet. Use "Add Employee" to create the first one.',
  );

  ngOnInit(): void {
    this.service.lookups().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (l) => this.lookups.set(l), error: () => undefined });
    for (const c of [this.designationControl, this.departmentControl, this.employmentStatusControl, this.typeControl, this.statusControl]) {
      c.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.filtersChanged());
    }
    this.searchControl.valueChanges.pipe(debounceTime(300), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.filtersChanged());
    this.load();
  }

  /** Any filter or the search changed: back to page 1. */
  protected filtersChanged(): void {
    this.filterVersion.update((v) => v + 1);
    this.page.set(1);
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .list(
        {
          search: this.searchControl.value.trim(),
          designationId: this.designationControl.value,
          departmentId: this.departmentControl.value,
          employmentStatusId: this.employmentStatusControl.value,
          employeeType: this.typeControl.value,
          status: this.statusControl.value,
        },
        this.page(),
        this.pageSize(),
      )
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
    for (const c of [this.designationControl, this.departmentControl, this.employmentStatusControl, this.typeControl, this.statusControl, this.searchControl]) c.setValue('', { emitEvent: false });
    this.filtersChanged();
  }

  protected add(): void {
    void this.router.navigate(['/employees/new']);
  }

  protected view(row: EmployeeRow): void {
    void this.router.navigate(['/employees', row.employeeId]);
  }

  protected edit(row: EmployeeRow): void {
    void this.router.navigate(['/employees', row.employeeId, 'edit']);
  }
}
