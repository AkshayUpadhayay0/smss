import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { catchError, finalize, of, switchMap, map } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { toUploadUrl } from '../../../../core/utils/upload-url';
import { ButtonComponent, CardComponent, InputComponent, PageHeaderComponent, SelectComponent, SelectOption, TextareaComponent } from '../../../../shared/components';
import { LogoChange, LogoUploadComponent } from '../../../schools/components/logo-upload/logo-upload.component';
import { firstErrorMessage, mobileValidator, requiredTrimmed } from '../../../schools/utils/school-validators';
import { EMPLOYEE_TYPES, Employee, EmployeeLookups, EmployeeRequest, EmployeeType } from '../../models/employee.model';
import { EmployeeService } from '../../services/employee.service';

const today = () => new Date().toISOString().slice(0, 10);

function pastDate(c: AbstractControl): ValidationErrors | null {
  return c.value && c.value >= today() ? { custom: 'Date of birth must be in the past' } : null;
}

/** Add (no id) or edit (route /employees/:employeeId/edit) one employee. */
@Component({
  selector: 'app-employee-form',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, InputComponent, LogoUploadComponent, PageHeaderComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './employee-form.component.scss',
  templateUrl: './employee-form.component.html',
})
export class EmployeeFormComponent implements OnInit {
  private readonly service = inject(EmployeeService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /** Bound from the :employeeId route param (withComponentInputBinding); absent when adding. */
  readonly employeeId = input<string>();

  protected readonly editing = computed(() => !!this.employeeId());
  protected readonly breadcrumbs = computed(() => [{ label: 'Employees', link: '/employees' }, { label: this.editing() ? 'Edit Employee' : 'Add Employee' }]);

  protected readonly typeOptions: SelectOption[] = EMPLOYEE_TYPES.map((t) => ({ label: t, value: t }));
  protected readonly lookups = signal<EmployeeLookups | null>(null);
  protected readonly designationOptions = computed(() => this.toOptions(this.lookups()?.designations));
  protected readonly departmentOptions = computed(() => this.toOptions(this.lookups()?.departments));
  protected readonly employmentStatusOptions = computed(() => this.toOptions(this.lookups()?.employmentStatuses));
  protected readonly genderOptions = computed(() => this.toOptions(this.lookups()?.genders));
  protected readonly bloodGroupOptions = computed(() => this.toOptions(this.lookups()?.bloodGroups));

  protected readonly form = new FormGroup({
    firstName: new FormControl('', { nonNullable: true, validators: [requiredTrimmed, Validators.maxLength(100)] }),
    middleName: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
    lastName: new FormControl('', { nonNullable: true, validators: [requiredTrimmed, Validators.maxLength(100)] }),
    dateOfBirth: new FormControl('', { nonNullable: true, validators: [Validators.required, pastDate] }),
    genderId: new FormControl('', { nonNullable: true }),
    bloodGroupId: new FormControl('', { nonNullable: true }),
    employeeType: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    designationId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    departmentId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    employmentStatusId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    dateOfJoining: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    dateOfLeaving: new FormControl('', { nonNullable: true }),
    qualification: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(1000)] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.email, Validators.maxLength(150)] }),
    mobileNumber: new FormControl('', { nonNullable: true, validators: [requiredTrimmed, mobileValidator] }),
    alternateMobile: new FormControl('', { nonNullable: true, validators: [mobileValidator] }),
    address: new FormControl('', { nonNullable: true, validators: [requiredTrimmed, Validators.maxLength(1000)] }),
  });

  protected readonly submitted = signal(false);
  protected readonly saving = signal(false);
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly existing = signal<Employee | null>(null);
  /** The photo picked for a NEW employee: uploaded right after the record is created. */
  private photo: File | null = null;
  protected readonly currentPhoto = computed(() => toUploadUrl(this.existing()?.photoUrl));

  ngOnInit(): void {
    this.service.lookups().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (l) => {
        this.lookups.set(l);
        // a new employee starts as "Active"
        if (!this.editing() && !this.form.controls.employmentStatusId.value) {
          const active = l.employmentStatuses.find((s) => s.name === 'Active');
          if (active) this.form.controls.employmentStatusId.setValue(String(active.id));
        }
      },
      error: () => this.loadFailed.set(true),
    });
    const id = this.employeeId();
    if (id) this.loadEmployee(id);
  }

  private loadEmployee(id: string): void {
    this.loading.set(true);
    this.service
      .get(id)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (e) => {
          this.existing.set(e);
          this.form.patchValue({
            firstName: e.firstName, middleName: e.middleName ?? '', lastName: e.lastName, dateOfBirth: e.dateOfBirth,
            genderId: e.genderId ? String(e.genderId) : '', bloodGroupId: e.bloodGroupId ? String(e.bloodGroupId) : '',
            employeeType: e.employeeType, designationId: String(e.designationId), departmentId: String(e.departmentId),
            employmentStatusId: String(e.employmentStatusId), dateOfJoining: e.dateOfJoining, dateOfLeaving: e.dateOfLeaving ?? '',
            qualification: e.qualification ?? '', email: e.email ?? '', mobileNumber: e.mobileNumber, alternateMobile: e.alternateMobile ?? '', address: e.address,
          });
        },
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  private toOptions(items: { id: number; name: string }[] | undefined): SelectOption[] {
    return (items ?? []).map((i) => ({ label: i.name, value: String(i.id) }));
  }

  protected err(name: keyof typeof this.form.controls): string | undefined {
    const control = this.form.controls[name];
    const custom = control.errors?.['custom'] as string | undefined;
    if (custom && (control.touched || this.submitted())) return custom;
    return firstErrorMessage(control, this.submitted());
  }

  protected onPhotoChanged(change: LogoChange): void {
    this.photo = change.file;
  }

  protected cancel(): void {
    void this.router.navigate(this.editing() ? ['/employees', this.employeeId()] : ['/employees']);
  }

  private request(): EmployeeRequest {
    const v = this.form.getRawValue();
    const num = (s: string) => (s ? Number(s) : null);
    return {
      designationId: Number(v.designationId), departmentId: Number(v.departmentId), employmentStatusId: num(v.employmentStatusId),
      employeeType: v.employeeType as EmployeeType, firstName: v.firstName.trim(), middleName: v.middleName.trim() || null, lastName: v.lastName.trim(),
      genderId: num(v.genderId), bloodGroupId: num(v.bloodGroupId), dateOfBirth: v.dateOfBirth, email: v.email.trim() || null,
      mobileNumber: v.mobileNumber.trim(), alternateMobile: v.alternateMobile.trim() || null, address: v.address.trim(),
      dateOfJoining: v.dateOfJoining, dateOfLeaving: v.dateOfLeaving || null, qualification: v.qualification.trim() || null,
    };
  }

  protected submit(): void {
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.saving()) return;
    const joining = this.form.controls.dateOfJoining.value;
    const leaving = this.form.controls.dateOfLeaving.value;
    if (leaving && joining && leaving < joining) this.form.controls.dateOfLeaving.setErrors({ custom: 'Date of leaving cannot be before the date of joining' });
    if (this.form.invalid) return;

    this.saving.set(true);
    const id = this.employeeId();
    const save$ = id ? this.service.update(id, this.request()) : this.service.create(this.request());
    const photo = id ? null : this.photo;
    save$
      .pipe(
        // the employee is saved first; a photo is a best-effort follow-up that must never undo or fail that
        switchMap((employee) =>
          photo
            ? this.service.uploadPhoto(employee.employeeId, photo).pipe(
                map(() => ({ employee, photoFailed: false })),
                catchError(() => of({ employee, photoFailed: true })),
              )
            : of({ employee, photoFailed: false }),
        ),
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ employee, photoFailed }) => {
          if (photoFailed) this.toast.warning('You can add the photo later from the employee page.', 'Employee saved, but the photo was not uploaded');
          else this.toast.success(id ? 'Employee updated successfully.' : `Employee ${employee.employeeId} created.`);
          void this.router.navigate(['/employees', employee.employeeId]);
        },
        error: () => undefined, // the server's reason (duplicate email, ...) is toasted by the interceptor; the form stays as it is
      });
  }
}
