import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent } from '../../../../shared/components';
import { CompanyEmployee } from '../../models/company-finance.model';
import { CompanyFinanceService } from '../../services/company-finance.service';

function leavingNotBeforeJoining(group: AbstractControl): ValidationErrors | null {
  const joined = group.get('dateOfJoining')?.value as string;
  const left = group.get('dateOfLeaving')?.value as string;
  return joined && left && left < joined ? { leavingBeforeJoining: true } : null;
}

const blankToNull = (v: string): string | null => (v.trim() === '' ? null : v.trim());

/** An email is optional: only a non-blank value has to look like one (Validators.email would reject "  "). */
const optionalEmail = (c: AbstractControl): ValidationErrors | null => (String(c.value ?? '').trim() === '' ? null : Validators.email(c));

/** Add (employee = null) or edit one of SMSS's own staff. The salary here is the CURRENT salary: salary payments already recorded keep their amounts. */
@Component({
  selector: 'app-company-employee-form-modal',
  imports: [ReactiveFormsModule, ButtonComponent, InputComponent, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .form { display: flex; flex-direction: column; gap: var(--space-4); }
    .row { display: grid; grid-template-columns: repeat(auto-fit, minmax(190px, 1fr)); gap: var(--space-4); }
  `,
  template: `
    <app-modal [title]="editing() ? 'Edit employee' : 'Add employee'" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" id="company-employee-form" (ngSubmit)="submit()" novalidate>
        <div class="row">
          <app-input label="First name" [required]="true" formControlName="firstName" [errorText]="err('firstName', 'Enter the first name')" />
          <app-input label="Last name" [required]="true" formControlName="lastName" [errorText]="err('lastName', 'Enter the last name')" />
        </div>
        <app-input label="Designation" [required]="true" placeholder="e.g. Developer, Support, Sales" formControlName="designation" [errorText]="err('designation', 'Enter the designation')" />
        <div class="row">
          <app-input label="Email" type="email" formControlName="email" [errorText]="submitted() && form.controls.email.invalid ? 'Enter a valid email address' : undefined" />
          <app-input label="Mobile" type="tel" formControlName="mobile" />
        </div>
        <div class="row">
          <app-input label="Date of joining" type="date" [required]="true" formControlName="dateOfJoining" [errorText]="err('dateOfJoining', 'Choose the joining date')" />
          <app-input label="Date of leaving" type="date" formControlName="dateOfLeaving" helpText="Leave blank while they work here."
            [errorText]="submitted() && form.hasError('leavingBeforeJoining') ? 'The leaving date cannot be before the joining date' : undefined" />
        </div>
        <app-input label="Monthly salary (₹)" type="number" [required]="true" formControlName="monthlySalary"
          helpText="The default for new salary payments. Payments already recorded are not changed."
          [errorText]="submitted() && form.controls.monthlySalary.invalid ? 'Enter a salary of 0 or more' : undefined" />
      </form>
      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button type="submit" [disabled]="saving()" (click)="submit()">{{ saving() ? 'Saving…' : editing() ? 'Save changes' : 'Add employee' }}</app-button>
      </ng-container>
    </app-modal>
  `,
})
export class CompanyEmployeeFormModalComponent implements OnInit {
  private readonly service = inject(CompanyFinanceService);
  private readonly toast = inject(ToastService);

  readonly employee = input<CompanyEmployee | null>(null);
  readonly saved = output<CompanyEmployee>();
  readonly closed = output<void>();

  protected readonly editing = computed(() => !!this.employee());
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected readonly form = new FormGroup(
    {
      firstName: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
      lastName: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
      designation: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
      email: new FormControl('', { nonNullable: true, validators: [optionalEmail, Validators.maxLength(150)] }),
      mobile: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(20)] }),
      dateOfJoining: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
      dateOfLeaving: new FormControl('', { nonNullable: true }),
      monthlySalary: new FormControl<number | string | null>(null, { validators: [Validators.required, Validators.min(0)] }),
    },
    { validators: [leavingNotBeforeJoining] },
  );

  ngOnInit(): void {
    const e = this.employee();
    if (e) {
      this.form.setValue({
        firstName: e.firstName, lastName: e.lastName, designation: e.designation, email: e.email ?? '', mobile: e.mobile ?? '',
        dateOfJoining: e.dateOfJoining, dateOfLeaving: e.dateOfLeaving ?? '', monthlySalary: e.monthlySalary,
      });
    }
  }

  protected err(name: 'firstName' | 'lastName' | 'designation' | 'dateOfJoining', message: string): string | undefined {
    return this.submitted() && this.form.controls[name].invalid ? message : undefined;
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    const request = {
      firstName: v.firstName.trim(), lastName: v.lastName.trim(), designation: v.designation.trim(), email: blankToNull(v.email), mobile: blankToNull(v.mobile),
      dateOfJoining: v.dateOfJoining, dateOfLeaving: blankToNull(v.dateOfLeaving), monthlySalary: Number(v.monthlySalary),
    };
    const e = this.employee();
    this.saving.set(true);
    (e ? this.service.updateEmployee(e.companyEmployeeId, request) : this.service.createEmployee(request))
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (saved) => {
          this.toast.success(e ? 'Employee updated.' : 'Employee added.');
          this.saved.emit(saved);
        },
        error: () => undefined, // the server's reason is toasted by the interceptor; the form stays as typed
      });
  }
}
