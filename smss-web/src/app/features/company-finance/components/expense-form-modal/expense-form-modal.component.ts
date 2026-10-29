import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { ExpenseCategory } from '../../models/company-finance.model';
import { CompanyFinanceService } from '../../services/company-finance.service';

const MODES: SelectOption[] = ['Cash', 'Cheque', 'UPI', 'Card', 'Bank Transfer', 'Online'].map((m) => ({ label: m, value: m }));
const isoToday = (): string => new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 10);

/** Logs one company expense. Straight in, no approval step: the Super Admin is the only user of this module. */
@Component({
  selector: 'app-expense-form-modal',
  imports: [ReactiveFormsModule, ButtonComponent, InputComponent, ModalComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .form { display: flex; flex-direction: column; gap: var(--space-4); }
    .row { display: grid; grid-template-columns: repeat(auto-fit, minmax(190px, 1fr)); gap: var(--space-4); }
  `,
  template: `
    <app-modal title="Add expense" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" id="expense-form" (ngSubmit)="submit()" novalidate>
        <div class="row">
          <app-input label="Date" type="date" [required]="true" formControlName="expenseDate" [errorText]="submitted() && form.controls.expenseDate.invalid ? 'Choose the date' : undefined" />
          <app-select label="Category" [required]="true" [options]="categoryOptions()" formControlName="categoryId" [errorText]="submitted() && form.controls.categoryId.invalid ? 'Choose a category' : undefined" />
        </div>
        <app-input label="Description" [required]="true" placeholder="What was this for?" formControlName="description" [errorText]="submitted() && form.controls.description.invalid ? 'Describe the expense (up to 500 characters)' : undefined" />
        <div class="row">
          <app-input label="Amount (₹)" type="number" [required]="true" formControlName="amount" [errorText]="submitted() && form.controls.amount.invalid ? 'Enter an amount greater than zero' : undefined" />
          <app-select label="Payment mode" [required]="true" [options]="modes" formControlName="paymentMode" [errorText]="submitted() && form.controls.paymentMode.invalid ? 'Choose a payment mode' : undefined" />
        </div>
      </form>
      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button type="submit" [disabled]="saving()" (click)="submit()">{{ saving() ? 'Saving…' : 'Add expense' }}</app-button>
      </ng-container>
    </app-modal>
  `,
})
export class ExpenseFormModalComponent implements OnInit {
  private readonly service = inject(CompanyFinanceService);
  private readonly toast = inject(ToastService);

  readonly categories = input<ExpenseCategory[]>([]);
  readonly saved = output<void>();
  readonly closed = output<void>();

  protected readonly modes = MODES;
  protected readonly categoryOptions = computed<SelectOption[]>(() => this.categories().map((c) => ({ label: c.categoryName, value: String(c.categoryId) })));
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected readonly form = new FormGroup({
    expenseDate: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    categoryId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(500), (c) => (String(c.value).trim() ? null : { required: true })] }),
    amount: new FormControl<number | string | null>(null, { validators: [Validators.required, Validators.min(0.01)] }),
    paymentMode: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  ngOnInit(): void {
    this.form.patchValue({ expenseDate: isoToday() });
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    this.saving.set(true);
    this.service
      .createExpense({ expenseDate: v.expenseDate, categoryId: Number(v.categoryId), description: v.description.trim(), amount: Number(v.amount), paymentMode: v.paymentMode })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.toast.success('Expense added.');
          this.saved.emit();
        },
        error: () => undefined,
      });
  }
}
