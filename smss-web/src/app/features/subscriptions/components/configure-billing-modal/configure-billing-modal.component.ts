import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, InputComponent, ModalComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { BillingFrequency, BillingTerms, SchoolSubscriptionRow, SubscriptionPricing } from '../../models/super-admin-dashboard.model';
import { SuperAdminDashboardService } from '../../services/super-admin-dashboard.service';

const FREQUENCIES: SelectOption[] = [
  { label: 'Monthly', value: 'MONTHLY' },
  { label: 'Yearly', value: 'YEARLY' },
];

/** A blank or non-numeric field means "use the global default". */
const blankToNull = (v: unknown): number | null => (v === null || v === undefined || String(v).trim() === '' || Number.isNaN(Number(v)) ? null : Number(v));

/**
 * One dialog in two modes. 'configure' moves a school out of its trial (billing frequency + optional terms); 'terms' changes the negotiated terms
 * of a school that is already configured. The terms are an optional custom rate, an optional custom yearly discount (both blank = the global
 * default, shown as the placeholder) and whether teachers and staff are billed like students. New terms apply to invoices generated afterwards.
 */
@Component({
  selector: 'app-configure-billing-modal',
  imports: [ReactiveFormsModule, ButtonComponent, InputComponent, ModalComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .form { display: flex; flex-direction: column; gap: var(--space-4); }
    .row { display: grid; grid-template-columns: repeat(auto-fit, minmax(190px, 1fr)); gap: var(--space-4); }
    .note { margin: 0; font-size: var(--text-sm); color: var(--color-text-muted); }
    fieldset { margin: 0; padding: 0; border: 0; display: flex; flex-direction: column; gap: var(--space-3); }
    legend { padding: 0; margin-bottom: var(--space-2); font-size: var(--text-sm); font-weight: var(--weight-semibold); }
    .check { display: flex; align-items: flex-start; gap: var(--space-3); font-size: var(--text-sm); cursor: pointer; }
    .check input { margin-top: 3px; accent-color: var(--color-primary); }
    .check small { display: block; color: var(--color-text-muted); }
  `,
  template: `
    <app-modal [title]="(isConfigure() ? 'Configure billing: ' : 'Billing terms: ') + school().schoolName" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" id="configure-billing-form" (ngSubmit)="submit()" novalidate>
        @if (isConfigure()) {
          <app-select
            label="Billing frequency"
            [required]="true"
            [options]="frequencies"
            formControlName="frequency"
            [errorText]="submitted() && form.controls.frequency.invalid ? 'Choose a billing frequency' : undefined"
          />
        }

        <fieldset>
          <legend>Pricing for this school</legend>
          <div class="row">
            <app-input
              label="Custom rate per student per month (₹)"
              type="number"
              formControlName="customRate"
              [placeholder]="pricing() ? 'Global: ' + inr(pricing()!.ratePerStudentMonthly) : 'Global rate'"
              [errorText]="rateError()"
            />
            <app-input
              label="Custom yearly discount (%)"
              type="number"
              formControlName="customDiscount"
              [placeholder]="pricing() ? 'Global: ' + pricing()!.yearlyDiscountPercent + '%' : 'Global discount'"
              [errorText]="discountError()"
            />
          </div>
          <p class="note">Leave a field blank to use the global default.@if (showsYearlyNote()) { The yearly discount only applies to yearly billing. }</p>
          <label class="check">
            <input type="checkbox" formControlName="includeStaff" />
            <span>Include teachers and staff in billing<small>Off: only students are billed. On: active teachers and staff are billed at the same rate as students.</small></span>
          </label>
        </fieldset>

        @if (isConfigure()) {
          <p class="note">
            This moves the school from trial to a paying subscription. The first billing cycle starts
            {{ school().trialExpired ? 'today, as the trial has ended' : 'the day after the trial ends (' + date(school().trialEndDate) + ')' }}.
          </p>
        } @else {
          <p class="note">New terms apply to invoices generated from now on. Invoices already generated keep the numbers they were issued with.</p>
        }
      </form>
      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button type="submit" [disabled]="saving()" (click)="submit()">{{ saving() ? 'Saving…' : isConfigure() ? 'Configure billing' : 'Save terms' }}</app-button>
      </ng-container>
    </app-modal>
  `,
})
export class ConfigureBillingModalComponent implements OnInit {
  private readonly service = inject(SuperAdminDashboardService);
  private readonly toast = inject(ToastService);

  readonly school = input.required<SchoolSubscriptionRow>();
  readonly mode = input<'configure' | 'terms'>('configure');
  /** The global pricing, shown as the placeholder of the custom fields. */
  readonly pricing = input<SubscriptionPricing | null>(null);
  readonly saved = output<void>();
  readonly closed = output<void>();

  protected readonly frequencies = FREQUENCIES;
  protected readonly inr = formatInr;
  protected readonly date = formatDate;
  protected readonly isConfigure = computed(() => this.mode() === 'configure');
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected readonly form = new FormGroup({
    frequency: new FormControl<BillingFrequency | ''>('', { nonNullable: true }),
    customRate: new FormControl<number | string | null>(null),
    customDiscount: new FormControl<number | string | null>(null),
    includeStaff: new FormControl(false, { nonNullable: true }),
  });
  private readonly frequencyPicked = toSignal(this.form.controls.frequency.valueChanges, { initialValue: '' as BillingFrequency | '' });
  protected readonly showsYearlyNote = computed(() => this.isConfigure() ? this.frequencyPicked() !== 'MONTHLY' : this.school().billingFrequency !== 'MONTHLY');

  ngOnInit(): void {
    const s = this.school();
    if (this.isConfigure()) this.form.controls.frequency.addValidators(Validators.required);
    this.form.patchValue({ customRate: s.customRatePerStudentMonthly, customDiscount: s.customYearlyDiscountPercent, includeStaff: s.includeStaffInBilling });
  }

  protected rateError(): string | undefined {
    const v = blankToNull(this.form.controls.customRate.value);
    return this.submitted() && v !== null && v < 0 ? 'The rate cannot be negative' : undefined;
  }

  protected discountError(): string | undefined {
    const v = blankToNull(this.form.controls.customDiscount.value);
    return this.submitted() && v !== null && (v < 0 || v > 100) ? 'Enter a percentage from 0 to 100' : undefined;
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.controls.frequency.updateValueAndValidity();
    if (this.form.controls.frequency.invalid || this.rateError() || this.discountError()) return;

    const v = this.form.getRawValue();
    const terms: BillingTerms = {
      customRatePerStudentMonthly: blankToNull(v.customRate),
      customYearlyDiscountPercent: blankToNull(v.customDiscount),
      includeStaffInBilling: v.includeStaff,
    };
    const school = this.school();
    this.saving.set(true);
    (this.isConfigure() ? this.service.configureBilling(school.schoolId, v.frequency as BillingFrequency, terms) : this.service.updateTerms(school.schoolId, terms))
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.toast.success(this.isConfigure() ? `Billing configured for ${school.schoolName}.` : `Billing terms saved for ${school.schoolName}.`);
          this.saved.emit();
        },
        error: () => undefined, // reason toasted by the interceptor
      });
  }
}
