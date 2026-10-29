import { ChangeDetectionStrategy, Component, OnInit, inject, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, InputComponent, ModalComponent, SelectComponent, SelectOption, TextareaComponent } from '../../../../shared/components';
import { MONTH_NAMES, CompanyEmployee, periodLabel } from '../../models/company-finance.model';
import { CompanyFinanceService } from '../../services/company-finance.service';

const MODES: SelectOption[] = ['Cash', 'Cheque', 'UPI', 'Card', 'Bank Transfer', 'Online'].map((m) => ({ label: m, value: m }));
const MONTHS: SelectOption[] = MONTH_NAMES.map((name, i) => ({ label: name, value: String(i + 1) }));
const isoToday = (): string => new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 10);

/**
 * Records one month's salary for one employee: a flat amount, locked once recorded. It starts at the employee's current monthly salary but can
 * be changed (a bonus or a partial month). One payment per employee per month; void the old one to pay the month again.
 */
@Component({
  selector: 'app-salary-payment-modal',
  imports: [ReactiveFormsModule, ButtonComponent, InputComponent, ModalComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .form { display: flex; flex-direction: column; gap: var(--space-4); }
    .row { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: var(--space-4); }
    .summary { margin: 0; padding: var(--space-3) var(--space-4); border-radius: var(--radius-md); background: var(--color-surface-muted); font-size: var(--text-sm); }
  `,
  template: `
    <app-modal [title]="'Record salary payment: ' + employee().fullName" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" id="salary-payment-form" (ngSubmit)="submit()" novalidate>
        <p class="summary">{{ employee().designation }} · current monthly salary <strong>{{ inr(employee().monthlySalary) }}</strong></p>
        <div class="row">
          <app-select label="Month" [required]="true" [options]="months" formControlName="month" />
          <app-select label="Year" [required]="true" [options]="years" formControlName="year" />
        </div>
        <app-input label="Amount (₹)" type="number" [required]="true" formControlName="amount"
          helpText="Starts at the current monthly salary. Change it for a bonus or a partial month."
          [errorText]="submitted() && form.controls.amount.invalid ? 'Enter an amount greater than zero' : undefined" />
        <div class="row">
          <app-input label="Payment date" type="date" [required]="true" formControlName="paymentDate" [errorText]="submitted() && form.controls.paymentDate.invalid ? 'Choose the payment date' : undefined" />
          <app-select label="Payment mode" [required]="true" [options]="modes" formControlName="paymentMode" [errorText]="submitted() && form.controls.paymentMode.invalid ? 'Choose a payment mode' : undefined" />
        </div>
        <app-textarea label="Remarks" placeholder="Optional" [rows]="2" formControlName="remarks" />
      </form>
      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button type="submit" [disabled]="saving()" (click)="submit()">{{ saving() ? 'Saving…' : 'Record payment' }}</app-button>
      </ng-container>
    </app-modal>
  `,
})
export class SalaryPaymentModalComponent implements OnInit {
  private readonly service = inject(CompanyFinanceService);
  private readonly toast = inject(ToastService);

  readonly employee = input.required<CompanyEmployee>();
  readonly saved = output<void>();
  readonly closed = output<void>();

  protected readonly inr = formatInr;
  protected readonly months = MONTHS;
  protected readonly modes = MODES;
  protected readonly years: SelectOption[] = (() => {
    const y = new Date().getFullYear();
    return [y + 1, y, y - 1, y - 2, y - 3].map((n) => ({ label: String(n), value: String(n) }));
  })();
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected readonly form = new FormGroup({
    month: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    year: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    amount: new FormControl<number | string | null>(null, { validators: [Validators.required, Validators.min(0.01)] }),
    paymentDate: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    paymentMode: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    remarks: new FormControl('', { nonNullable: true }),
  });

  ngOnInit(): void {
    const now = new Date();
    this.form.patchValue({ month: String(now.getMonth() + 1), year: String(now.getFullYear()), amount: this.employee().monthlySalary, paymentDate: isoToday() });
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    const month = Number(v.month);
    const year = Number(v.year);
    this.saving.set(true);
    this.service
      .recordSalary({
        companyEmployeeId: this.employee().companyEmployeeId, periodMonth: month, periodYear: year, amount: Number(v.amount),
        paymentDate: v.paymentDate, paymentMode: v.paymentMode, remarks: v.remarks.trim() || null,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.toast.success(`Salary for ${periodLabel(month, year)} recorded for ${this.employee().fullName}.`);
          this.saved.emit();
        },
        error: () => undefined, // e.g. already paid for that month: the server's reason is toasted
      });
  }
}
