import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize, map, startWith } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { ConcessionType, FeeConcession } from '../../models/fee-concession.model';
import { FeeTypeOption } from '../../models/fee-structure.model';
import { FeeConcessionService } from '../../services/fee-concession.service';
import { FeeStructureService } from '../../services/fee-structure.service';

const MAX_VALUE = 99_999_999.99;

const notBlank = (c: AbstractControl): ValidationErrors | null => (String(c.value ?? '').trim() === '' ? { blank: true } : null);

/**
 * Add / edit a fee concession. The value rule follows the type: a percentage is more than 0 and at most 100,
 * a fixed amount is more than 0 with no upper limit. Status is a row action on the list, never a form field.
 */
@Component({
  selector: 'app-fee-concession-form-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, InputComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './fee-concession-form-modal.component.scss',
  template: `
    <app-modal [title]="isEdit() ? 'Edit Fee Concession' : 'Add Fee Concession'" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <app-input label="Concession name" [required]="true" placeholder="e.g. Sibling Discount" formControlName="name" [errorText]="error('name')" />
        <app-input label="Concession code" [required]="true" placeholder="e.g. SIBLING" formControlName="code" [errorText]="error('code')" />

        <fieldset class="type" [attr.aria-describedby]="'type-help'">
          <legend>Type <span class="required">*</span></legend>
          <label class="option" [class.selected]="type() === 'PERCENTAGE'">
            <input type="radio" formControlName="type" value="PERCENTAGE" />
            <span>Percentage (%)</span>
          </label>
          <label class="option" [class.selected]="type() === 'FIXED'">
            <input type="radio" formControlName="type" value="FIXED" />
            <span>Fixed amount (₹)</span>
          </label>
        </fieldset>

        <app-input
          [label]="type() === 'PERCENTAGE' ? 'Value (%)' : 'Value (₹)'"
          type="number"
          [required]="true"
          [placeholder]="type() === 'PERCENTAGE' ? 'e.g. 15' : 'e.g. 2000'"
          [helpText]="type() === 'PERCENTAGE' ? 'More than 0 and at most 100.' : 'More than 0.'"
          formControlName="value"
          [errorText]="error('value')"
        />

        <app-select
          label="Applies to"
          placeholder="All Fee Heads"
          [options]="feeTypeOptions()"
          formControlName="feeTypeId"
          [errorText]="error('feeTypeId')"
        />
        @if (feeTypesFailed()) {
          <p class="note">The fee types could not be loaded, so this can only apply to all fee heads right now.</p>
        }

        <!-- lets Enter submit; the visible button lives in the modal footer -->
        <button type="submit" hidden tabindex="-1"></button>
      </form>

      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button [disabled]="saving()" (click)="submit()">{{ saving() ? 'Saving…' : isEdit() ? 'Update' : 'Create' }}</app-button>
      </ng-container>
    </app-modal>
  `,
})
export class FeeConcessionFormModalComponent implements OnInit {
  private readonly service = inject(FeeConcessionService);
  private readonly structures = inject(FeeStructureService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  /** The concession being edited, or null to create. */
  readonly concession = input<FeeConcession | null>(null);
  readonly saved = output<FeeConcession>();
  readonly closed = output<void>();

  protected readonly isEdit = computed(() => this.concession() !== null);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);
  protected readonly feeTypes = signal<FeeTypeOption[]>([]);
  protected readonly feeTypesFailed = signal(false);

  protected readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(100)] }),
    code: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, notBlank, Validators.maxLength(30), Validators.pattern(/^[A-Za-z0-9_-]+$/)],
    }),
    type: new FormControl<ConcessionType>('PERCENTAGE', { nonNullable: true }),
    value: new FormControl('', { nonNullable: true }),
    feeTypeId: new FormControl('', { nonNullable: true }),
  });

  protected readonly type = toSignal(this.form.controls.type.valueChanges.pipe(startWith(this.form.controls.type.value), map((t) => t)), {
    requireSync: true,
  });

  /** Active fee types, plus the one this concession already points at even if it has since been deactivated. */
  protected readonly feeTypeOptions = computed<SelectOption[]>(() => {
    const own = this.concession()?.applicableFeeTypeId;
    return this.feeTypes()
      .filter((t) => t.isActive || t.feeTypeId === own)
      .map((t) => ({ label: t.feeTypeName + (t.isActive ? '' : ' (inactive)'), value: String(t.feeTypeId) }));
  });

  constructor() {
    this.applyValueRules();
    // The value's limits depend on the type, so re-check it whenever the type changes
    this.form.controls.type.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.form.controls.value.updateValueAndValidity());
  }

  ngOnInit(): void {
    const c = this.concession();
    if (c) {
      this.form.patchValue({
        name: c.concessionName,
        code: c.concessionCode,
        type: c.concessionType,
        value: String(c.value),
        feeTypeId: c.applicableFeeTypeId ? String(c.applicableFeeTypeId) : '',
      });
    }

    this.structures
      .feeTypes()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => this.feeTypes.set(list),
        error: () => this.feeTypesFailed.set(true), // already toasted by the interceptor
      });
  }

  private applyValueRules(): void {
    const value = this.form.controls.value;
    value.setValidators([
      Validators.required,
      (control) => {
        const raw = String(control.value ?? '').trim();
        if (raw === '') return null; // `required` reports blanks
        const n = Number(raw);
        if (!Number.isFinite(n) || n <= 0) return { value: 'Enter a value greater than 0' };
        if (this.form.controls.type.value === 'PERCENTAGE' && n > 100) return { value: 'A percentage cannot be more than 100' };
        if (n > MAX_VALUE) return { value: 'Value is too large' };
        if (!/^\d+(\.\d{1,2})?$/.test(raw)) return { value: 'Use at most 2 decimal places' };
        return null;
      },
    ]);
  }

  protected error(name: 'name' | 'code' | 'value' | 'feeTypeId'): string | undefined {
    const c = this.form.controls[name];
    if (c.valid || !(c.touched || this.submitted())) return undefined;
    if (c.hasError('required') || c.hasError('blank')) return 'This field is required';
    if (c.hasError('maxlength')) return `Maximum ${c.getError('maxlength').requiredLength} characters`;
    if (c.hasError('pattern')) return 'Use only letters, numbers, hyphen and underscore';
    if (c.hasError('value')) return c.getError('value');
    return 'Invalid value';
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    const v = this.form.getRawValue();
    const body = {
      concessionName: v.name.trim(),
      concessionCode: v.code.trim(),
      concessionType: v.type,
      value: Number(v.value),
      applicableFeeTypeId: v.feeTypeId ? Number(v.feeTypeId) : null,
    };
    const editing = this.concession();

    this.saving.set(true);
    (editing ? this.service.update(editing.concessionId, body) : this.service.create(body))
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (saved) => {
          this.toast.success(`Fee concession ${editing ? 'updated' : 'created'} successfully.`);
          this.saved.emit(saved);
        },
        // Already toasted by the interceptor (e.g. the 409 for a duplicate code); the modal stays open.
        error: () => undefined,
      });
  }
}
