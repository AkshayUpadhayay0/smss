import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize, map, startWith } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { Vehicle, VehicleLookups, normalizeCode } from '../../models/transport.model';
import { TransportService } from '../../services/transport.service';

type Field =
  | 'vehicleLabel' | 'registrationNumber' | 'vehicleTypeId' | 'ownershipId' | 'modelYear' | 'seatingCapacity'
  | 'ownerName' | 'ownerContact' | 'lastServiceDate' | 'pucExpiryDate' | 'insuranceExpiryDate';

const PLATE = /^[A-Z0-9-]{4,20}$/;

/** One vehicle. The owner / vendor fields appear only for a rented ownership (the server rejects them on an owned vehicle). */
@Component({
  selector: 'app-vehicle-form-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, InputComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal [title]="isEdit() ? 'Edit Vehicle' : 'Add Vehicle'" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <div class="grid">
          <app-input label="Vehicle number / label" [required]="true" placeholder="e.g. Bus 1" formControlName="vehicleLabel" [errorText]="error('vehicleLabel')" />
          <app-input
            label="Registration number"
            [required]="true"
            placeholder="e.g. UP32AB1234"
            helpText="Letters, digits and hyphens, 4 to 20 characters. Spaces are removed and letters made capital."
            formControlName="registrationNumber"
            [errorText]="error('registrationNumber')"
          />
          <app-select label="Vehicle type" [required]="true" [options]="typeOptions()" formControlName="vehicleTypeId" [errorText]="error('vehicleTypeId')" />
          <app-select label="Ownership" [required]="true" [options]="ownershipOptions()" formControlName="ownershipId" [errorText]="error('ownershipId')" />
          <app-input label="Model year" type="number" placeholder="Optional, e.g. 2019" formControlName="modelYear" [errorText]="error('modelYear')" />
          <app-input label="Seating capacity" type="number" placeholder="Optional, e.g. 40" formControlName="seatingCapacity" [errorText]="error('seatingCapacity')" />
          @if (rented()) {
            <app-input label="Owner / vendor name" placeholder="Optional" formControlName="ownerName" [errorText]="error('ownerName')" />
            <app-input label="Owner / vendor contact" placeholder="Optional" formControlName="ownerContact" [errorText]="error('ownerContact')" />
          }
          <app-input label="Last service date" type="date" formControlName="lastServiceDate" [errorText]="error('lastServiceDate')" />
          <app-input label="PUC expiry date" type="date" formControlName="pucExpiryDate" [errorText]="error('pucExpiryDate')" />
          <app-input label="Insurance expiry date" type="date" formControlName="insuranceExpiryDate" [errorText]="error('insuranceExpiryDate')" />
        </div>
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
    .form .grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--space-5);
    }
    @media (max-width: 640px) {
      .form .grid {
        grid-template-columns: 1fr;
      }
    }
  `,
})
export class VehicleFormModalComponent implements OnInit {
  private readonly service = inject(TransportService);
  private readonly toast = inject(ToastService);

  /** The vehicle being edited, or null to create. */
  readonly vehicle = input<Vehicle | null>(null);
  /** Vehicle types and ownerships, as the server returned them. */
  readonly lookups = input.required<VehicleLookups>();
  readonly saved = output<Vehicle>();
  readonly closed = output<void>();

  protected readonly isEdit = computed(() => this.vehicle() !== null);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected readonly typeOptions = computed<SelectOption[]>(() => this.lookups().vehicleTypes.map((t) => ({ label: t.name, value: String(t.id) })));
  protected readonly ownershipOptions = computed<SelectOption[]>(() => this.lookups().ownerships.map((o) => ({ label: o.name, value: String(o.id) })));

  protected readonly form = new FormGroup({
    vehicleLabel: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(50)] }),
    registrationNumber: new FormControl('', { nonNullable: true, validators: [Validators.required, plate] }),
    vehicleTypeId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    ownershipId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    modelYear: new FormControl('', { nonNullable: true, validators: [whole(1950, new Date().getFullYear() + 1, 'year')] }),
    seatingCapacity: new FormControl('', { nonNullable: true, validators: [whole(1, 200, 'seats')] }),
    ownerName: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(150)] }),
    ownerContact: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(20)] }),
    lastServiceDate: new FormControl('', { nonNullable: true, validators: [notFuture] }),
    pucExpiryDate: new FormControl('', { nonNullable: true }),
    insuranceExpiryDate: new FormControl('', { nonNullable: true }),
  });

  private readonly ownershipId = toSignal(this.form.controls.ownershipId.valueChanges.pipe(startWith(''), map((v) => v)), { initialValue: '' });
  /** True when the chosen ownership is a rented / contract one. */
  protected readonly rented = computed(() => this.lookups().ownerships.find((o) => String(o.id) === this.ownershipId())?.isRented ?? false);

  ngOnInit(): void {
    const v = this.vehicle();
    if (!v) return;
    this.form.patchValue({
      vehicleLabel: v.vehicleLabel,
      registrationNumber: v.registrationNumber,
      vehicleTypeId: String(v.vehicleTypeId),
      ownershipId: String(v.ownershipId),
      modelYear: v.modelYear == null ? '' : String(v.modelYear),
      seatingCapacity: v.seatingCapacity == null ? '' : String(v.seatingCapacity),
      ownerName: v.ownerName ?? '',
      ownerContact: v.ownerContact ?? '',
      lastServiceDate: v.lastServiceDate ?? '',
      pucExpiryDate: v.pucExpiryDate ?? '',
      insuranceExpiryDate: v.insuranceExpiryDate ?? '',
    });
  }

  protected error(name: Field): string | undefined {
    const c = this.form.controls[name];
    if (!(c.touched || this.submitted())) return undefined;
    if (c.hasError('required') || c.hasError('blank')) return 'This field is required';
    if (c.hasError('maxlength')) return `Maximum ${c.getError('maxlength').requiredLength} characters`;
    if (c.hasError('plate')) return 'Use 4 to 20 capital letters, digits or hyphens';
    if (c.hasError('year')) return `Enter a year from 1950 to ${new Date().getFullYear() + 1}`;
    if (c.hasError('seats')) return 'Enter a whole number from 1 to 200';
    if (c.hasError('future')) return 'The last service date cannot be in the future';
    return undefined;
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    const v = this.form.getRawValue();
    const rented = this.rented();
    const body = {
      vehicleLabel: v.vehicleLabel.trim(),
      registrationNumber: normalizeCode(v.registrationNumber),
      vehicleTypeId: Number(v.vehicleTypeId),
      ownershipId: Number(v.ownershipId),
      modelYear: v.modelYear.trim() === '' ? null : Number(v.modelYear),
      seatingCapacity: v.seatingCapacity.trim() === '' ? null : Number(v.seatingCapacity),
      // Owner details only travel for a rented vehicle (the server refuses them on an owned one)
      ownerName: rented ? v.ownerName.trim() || null : null,
      ownerContact: rented ? v.ownerContact.trim() || null : null,
      lastServiceDate: v.lastServiceDate || null,
      pucExpiryDate: v.pucExpiryDate || null,
      insuranceExpiryDate: v.insuranceExpiryDate || null,
    };
    const editing = this.vehicle();

    this.saving.set(true);
    (editing ? this.service.updateVehicle(editing.vehicleId, body) : this.service.createVehicle(body))
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (saved) => {
          this.toast.success(`Vehicle ${editing ? 'updated' : 'created'} successfully.`);
          this.saved.emit(saved);
        },
        // Already toasted by the interceptor (e.g. the 409 for a duplicate plate); the modal stays open.
        error: () => undefined,
      });
  }
}

function notBlank(control: AbstractControl): ValidationErrors | null {
  return String(control.value ?? '').trim() === '' ? { blank: true } : null;
}

function plate(control: AbstractControl): ValidationErrors | null {
  const raw = normalizeCode(control.value);
  return raw === '' || PLATE.test(raw) ? null : { plate: true };
}

function whole(min: number, max: number, key: string) {
  return (control: AbstractControl): ValidationErrors | null => {
    const raw = String(control.value ?? '').trim();
    if (raw === '') return null;
    const n = Number(raw);
    return Number.isInteger(n) && n >= min && n <= max ? null : { [key]: true };
  };
}

function notFuture(control: AbstractControl): ValidationErrors | null {
  const raw = String(control.value ?? '');
  if (!raw) return null;
  const now = new Date();
  const today = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
  return raw > today ? { future: true } : null;
}
