import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, TextareaComponent } from '../../../../shared/components';
import { TransportArea } from '../../models/transport.model';
import { TransportService } from '../../services/transport.service';

type Field = 'areaName' | 'distanceKm' | 'transportFee' | 'remarks';

/** Name, distance, fee and remarks of one transport area. Status is a row action on the list, never a form field. */
@Component({
  selector: 'app-area-form-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, InputComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal [title]="isEdit() ? 'Edit Area' : 'Add Area'" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <app-input label="Area name" [required]="true" placeholder="e.g. Civil Lines" formControlName="areaName" [errorText]="error('areaName')" />
        <app-input label="Distance (km)" type="number" placeholder="Optional, e.g. 6.5" formControlName="distanceKm" [errorText]="error('distanceKm')" />
        <app-input
          label="Transport fee"
          type="number"
          [required]="true"
          placeholder="e.g. 800"
          helpText="Fee amount for this area. How often it is charged follows your school's Transport fee type."
          formControlName="transportFee"
          [errorText]="error('transportFee')"
        />
        <app-textarea label="Remarks" placeholder="Optional" [maxLength]="500" formControlName="remarks" [errorText]="error('remarks')" />
        <!-- lets Enter submit; the visible button lives in the modal footer -->
        <button type="submit" hidden tabindex="-1"></button>
      </form>

      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button [disabled]="saving()" (click)="submit()">{{ saving() ? 'Saving…' : isEdit() ? 'Update' : 'Create' }}</app-button>
      </ng-container>
    </app-modal>
  `,
  styles: `
    .form {
      display: flex;
      flex-direction: column;
      gap: var(--space-5);
    }
  `,
})
export class AreaFormModalComponent implements OnInit {
  private readonly service = inject(TransportService);
  private readonly toast = inject(ToastService);

  /** The area being edited, or null to create. */
  readonly area = input<TransportArea | null>(null);
  readonly saved = output<TransportArea>();
  readonly closed = output<void>();

  protected readonly isEdit = computed(() => this.area() !== null);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected readonly form = new FormGroup({
    areaName: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(150)] }),
    distanceKm: new FormControl('', { nonNullable: true, validators: [numberBetween(0, 9999.99, 'distance')] }),
    transportFee: new FormControl('', { nonNullable: true, validators: [Validators.required, numberBetween(0, 99999999.99, 'fee')] }),
    remarks: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
  });

  ngOnInit(): void {
    const a = this.area();
    if (a) {
      this.form.patchValue({
        areaName: a.areaName,
        distanceKm: a.distanceKm == null ? '' : String(a.distanceKm),
        transportFee: String(a.transportFee),
        remarks: a.remarks ?? '',
      });
    }
  }

  protected error(name: Field): string | undefined {
    const c = this.form.controls[name];
    if (!(c.touched || this.submitted())) return undefined;
    if (c.hasError('required') || c.hasError('blank')) return 'This field is required';
    if (c.hasError('maxlength')) return `Maximum ${c.getError('maxlength').requiredLength} characters`;
    if (c.hasError('distance')) return 'Enter a distance from 0 to 9999.99 km';
    if (c.hasError('fee')) return 'Enter a fee of 0 or more';
    return undefined;
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    const v = this.form.getRawValue();
    const body = {
      areaName: v.areaName.trim(),
      distanceKm: v.distanceKm.trim() === '' ? null : Number(v.distanceKm),
      transportFee: Number(v.transportFee),
      remarks: v.remarks.trim() || null,
    };
    const editing = this.area();

    this.saving.set(true);
    (editing ? this.service.updateArea(editing.areaId, body) : this.service.createArea(body))
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (saved) => {
          this.toast.success(`Area ${editing ? 'updated' : 'created'} successfully.`);
          this.saved.emit(saved);
        },
        // Already toasted by the interceptor (e.g. the 409 for a duplicate name); the modal stays open.
        error: () => undefined,
      });
  }
}

function notBlank(control: AbstractControl): ValidationErrors | null {
  return String(control.value ?? '').trim() === '' ? { blank: true } : null;
}

/** An optional-or-required number (`required` reports blanks) within [min, max]; the error key names the field. */
function numberBetween(min: number, max: number, key: string) {
  return (control: AbstractControl): ValidationErrors | null => {
    const raw = String(control.value ?? '').trim();
    if (raw === '') return null;
    const n = Number(raw);
    return Number.isFinite(n) && n >= min && n <= max ? null : { [key]: true };
  };
}
