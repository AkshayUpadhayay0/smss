import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, TextareaComponent } from '../../../../shared/components';
import { TaxType } from '../../models/tax-type.model';
import { TaxTypeService } from '../../services/tax-type.service';

const notBlank = (c: AbstractControl): ValidationErrors | null => (String(c.value ?? '').trim() === '' ? { blank: true } : null);

/** A rate is 0 to 100 with at most 2 decimals (the server and the table enforce the same). */
export const rateRule = (c: AbstractControl): ValidationErrors | null => {
  const raw = String(c.value ?? '').trim();
  if (raw === '') return null; // `required` reports blanks
  const n = Number(raw);
  if (!Number.isFinite(n)) return { rate: 'Enter a number' };
  if (n < 0 || n > 100) return { rate: 'Rate must be between 0 and 100' };
  if (!/^\d+(\.\d{1,2})?$/.test(raw)) return { rate: 'Use at most 2 decimal places' };
  return null;
};

/**
 * Add / edit a tax type. Editing the rate is allowed: it affects only NEW payments, because every payment keeps its own snapshot.
 * Status is a row action on the list, never a form field.
 */
@Component({
  selector: 'app-tax-type-form-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, InputComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './tax-type-form-modal.component.scss',
  template: `
    <app-modal [title]="isEdit() ? 'Edit Tax Type' : 'Add Tax Type'" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <app-input label="Tax name" [required]="true" placeholder="e.g. GST" formControlName="name" [errorText]="error('name')" />
        <app-input label="Tax code" [required]="true" placeholder="e.g. GST5" formControlName="code" [errorText]="error('code')" />
        <app-input
          label="Rate (%)"
          type="number"
          [required]="true"
          placeholder="e.g. 18"
          helpText="0 to 100, up to 2 decimals."
          formControlName="rate"
          [errorText]="error('rate')"
        />
        @if (isEdit()) {
          <p class="note">A new rate applies only to payments taken from now on. Receipts already issued keep the rate they were taken at.</p>
        }
        <app-textarea label="Description" placeholder="Optional" [maxLength]="500" formControlName="description" />

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
export class TaxTypeFormModalComponent implements OnInit {
  private readonly service = inject(TaxTypeService);
  private readonly toast = inject(ToastService);

  /** The tax type being edited, or null to create. */
  readonly taxType = input<TaxType | null>(null);
  readonly saved = output<TaxType>();
  readonly closed = output<void>();

  protected readonly isEdit = computed(() => this.taxType() !== null);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(100)] }),
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(30), Validators.pattern(/^[A-Za-z0-9_-]+$/)] }),
    rate: new FormControl('', { nonNullable: true, validators: [Validators.required, rateRule] }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
  });

  ngOnInit(): void {
    const t = this.taxType();
    if (t) this.form.patchValue({ name: t.taxName, code: t.taxCode, rate: String(t.ratePercent), description: t.description ?? '' });
  }

  protected error(name: 'name' | 'code' | 'rate'): string | undefined {
    const c = this.form.controls[name];
    if (c.valid || !(c.touched || this.submitted())) return undefined;
    if (c.hasError('required') || c.hasError('blank')) return 'This field is required';
    if (c.hasError('maxlength')) return `Maximum ${c.getError('maxlength').requiredLength} characters`;
    if (c.hasError('pattern')) return 'Use only letters, numbers, hyphen and underscore';
    if (c.hasError('rate')) return c.getError('rate');
    return 'Invalid value';
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    const v = this.form.getRawValue();
    const body = { taxName: v.name.trim(), taxCode: v.code.trim(), ratePercent: Number(v.rate), description: v.description.trim() || null };
    const editing = this.taxType();

    this.saving.set(true);
    (editing ? this.service.update(editing.taxTypeId, body) : this.service.create(body))
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (saved) => {
          this.toast.success(`Tax type ${editing ? 'updated' : 'created'} successfully.`);
          this.saved.emit(saved);
        },
        // Already toasted by the interceptor (e.g. the 409 for a duplicate name or code); the modal stays open.
        error: () => undefined,
      });
  }
}
