import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Subscription, debounceTime, distinctUntilChanged, finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, SelectComponent, SelectOption, TableColumn, TableComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { CompanyEmployeeFormModalComponent } from '../../components/employee-form-modal/employee-form-modal.component';
import { SalaryHistoryModalComponent } from '../../components/salary-history-modal/salary-history-modal.component';
import { SalaryPaymentModalComponent } from '../../components/salary-payment-modal/salary-payment-modal.component';
import { CompanyEmployee } from '../../models/company-finance.model';
import { CompanyFinanceService } from '../../services/company-finance.service';

interface EmployeeRow extends CompanyEmployee {
  contactText: string;
  joinedText: string;
  salaryText: string;
  [key: string]: unknown;
}

const STATUS_FILTERS: SelectOption[] = [
  { label: 'All employees', value: '' },
  { label: 'Active', value: 'Active' },
  { label: 'Inactive', value: 'Inactive' },
];

/**
 * /company/employees (Super Admin): SMSS's own team. Add, edit and deactivate (never delete), record a month's salary, and see a person's
 * salary payments (with void). The list is small, so it is not paged.
 */
@Component({
  selector: 'app-company-employees-page',
  imports: [
    ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, SelectComponent, TableComponent,
    CompanyEmployeeFormModalComponent, SalaryPaymentModalComponent, SalaryHistoryModalComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './employees-page.component.scss',
  templateUrl: './employees-page.component.html',
})
export class CompanyEmployeesPageComponent implements OnInit {
  private readonly service = inject(CompanyFinanceService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmDialogService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Company Finance' }, { label: 'Employees' }];
  protected readonly statusFilters = STATUS_FILTERS;

  protected readonly searchControl = new FormControl('', { nonNullable: true });
  protected readonly statusControl = new FormControl<'Active' | 'Inactive' | ''>('', { nonNullable: true });
  protected readonly employees = signal<CompanyEmployee[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  private request?: Subscription;

  protected readonly editing = signal<CompanyEmployee | 'new' | null>(null);
  protected readonly paying = signal<CompanyEmployee | null>(null);
  protected readonly history = signal<CompanyEmployee | null>(null);
  protected readonly busyId = signal<number | null>(null);

  protected readonly columns: TableColumn<EmployeeRow>[] = [
    { key: 'fullName', label: 'Name' },
    { key: 'designation', label: 'Designation' },
    { key: 'contactText', label: 'Contact' },
    { key: 'joinedText', label: 'Joined' },
    { key: 'salaryText', label: 'Monthly salary' },
    { key: 'statusName', label: 'Status', type: 'status' },
  ];
  protected readonly rows = computed<EmployeeRow[]>(() =>
    this.employees().map((e) => ({
      ...e,
      contactText: [e.email, e.mobile].filter(Boolean).join(' · ') || '—',
      joinedText: e.dateOfLeaving ? `${formatDate(e.dateOfJoining)} – ${formatDate(e.dateOfLeaving)}` : formatDate(e.dateOfJoining),
      salaryText: formatInr(e.monthlySalary),
    })),
  );

  ngOnInit(): void {
    this.searchControl.valueChanges.pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
    this.statusControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
    this.load();
  }

  protected load(): void {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.request = this.service
      .employees(this.searchControl.value.trim(), this.statusControl.value)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => { this.employees.set(list); this.failed.set(false); },
        error: () => { this.employees.set([]); this.failed.set(true); },
      });
  }

  protected onSaved(): void {
    this.editing.set(null);
    this.load();
  }

  protected async toggle(e: CompanyEmployee): Promise<void> {
    if (this.busyId() !== null) return;
    const deactivating = e.statusName === 'Active';
    const ok = await this.confirm.confirm({
      title: deactivating ? 'Deactivate employee' : 'Activate employee',
      message: deactivating
        ? `Deactivate ${e.fullName}? They are hidden from new salary payments. Nothing is deleted, and you can activate them again.`
        : `Activate ${e.fullName} again?`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!ok) return;
    this.busyId.set(e.companyEmployeeId);
    this.service
      .toggleEmployeeStatus(e.companyEmployeeId)
      .pipe(finalize(() => this.busyId.set(null)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.toast.success(updated.statusName === 'Active' ? `${updated.fullName} activated.` : `${updated.fullName} deactivated.`);
          this.load();
        },
        error: () => undefined,
      });
  }
}
