import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { toUploadUrl } from '../../../../core/utils/upload-url';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { EmployeeDocumentsComponent } from '../../components/employee-documents/employee-documents.component';
import { EmployeeLoginDialogComponent } from '../../components/employee-login-dialog/employee-login-dialog.component';
import { EmployeePhotoDialogComponent } from '../../components/employee-photo-dialog/employee-photo-dialog.component';
import { Employee, EmployeeLoginCreated, EmployeeLookups, EmployeePhoto, employmentStatusTone } from '../../models/employee.model';
import { EmployeeService } from '../../services/employee.service';

/** One employee: details, photo, documents and the login. */
@Component({
  selector: 'app-employee-detail',
  imports: [ButtonComponent, CardComponent, EmployeeDocumentsComponent, EmployeeLoginDialogComponent, EmployeePhotoDialogComponent, IconComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './employee-detail.component.scss',
  templateUrl: './employee-detail.component.html',
})
export class EmployeeDetailComponent implements OnInit {
  private readonly service = inject(EmployeeService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly destroyRef = inject(DestroyRef);

  /** Bound from the :employeeId route param (withComponentInputBinding). */
  readonly employeeId = input.required<string>();

  protected readonly employee = signal<Employee | null>(null);
  protected readonly lookups = signal<EmployeeLookups | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal(false);
  protected readonly photoOpen = signal(false);
  protected readonly loginOpen = signal(false);

  protected readonly fmt = formatDate;
  protected readonly tone = employmentStatusTone;
  protected readonly breadcrumbs = computed(() => [{ label: 'Employees', link: '/employees' }, { label: this.employee()?.fullName ?? 'Employee' }]);
  protected readonly photoUrl = computed(() => toUploadUrl(this.employee()?.photoUrl));
  protected readonly isActive = computed(() => this.employee()?.statusName === 'Active');
  protected readonly initials = computed(() =>
    (this.employee()?.fullName ?? '').split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]!.toUpperCase()).join('') || '?',
  );

  ngOnInit(): void {
    this.load();
    this.service.lookups().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (l) => this.lookups.set(l), error: () => undefined });
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .get(this.employeeId())
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (e) => this.employee.set(e),
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  protected back(): void {
    void this.router.navigate(['/employees']);
  }

  protected edit(): void {
    void this.router.navigate(['/employees', this.employeeId(), 'edit']);
  }

  protected async toggleStatus(): Promise<void> {
    const e = this.employee();
    if (!e || this.busy()) return;
    const deactivate = this.isActive();
    const ok = await this.confirmDialog.confirm({
      title: deactivate ? 'Deactivate employee?' : 'Activate employee?',
      message: deactivate
        ? `${e.fullName} will be hidden as an inactive record${e.loginId ? ' and their login will stop working' : ''}. Use this for a duplicate or a mistaken record; for someone who has left, set their Employment status instead.`
        : `${e.fullName} will be active again${e.loginId ? ' and their login will work again' : ''}.`,
      confirmText: deactivate ? 'Deactivate' : 'Activate',
      variant: deactivate ? 'danger' : 'primary',
    });
    if (!ok) return;
    this.busy.set(true);
    this.service
      .toggleStatus(e.employeeId)
      .pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.employee.set(updated);
          this.toast.success(updated.statusName === 'Active' ? 'Employee activated.' : 'Employee deactivated.');
        },
        error: () => undefined,
      });
  }

  protected onPhotoSaved(photo: EmployeePhoto): void {
    this.photoOpen.set(false);
    this.employee.update((e) => (e ? { ...e, photoUrl: photo.photoUrl ?? null } : e));
  }

  protected onLoginCreated(_: EmployeeLoginCreated): void {
    this.loginOpen.set(false);
    this.load(); // the login id and roles now show
  }
}
