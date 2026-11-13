import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, SelectComponent, SelectOption, TextareaComponent } from '../../../../shared/components';
import { Driver, DriverLookups, normalizeCode } from '../../models/transport.model';
import { TransportService } from '../../services/transport.service';

type Field = 'fullName' | 'mobileNumber' | 'email' | 'address' | 'dlNumber' | 'dlValidUpto';

const MOBILE = /^[6-9]\d{9}$/;
const DL = /^[A-Z0-9/-]{5,30}$/;

/** One driver: personal details, licence, the vehicle they drive and the areas they cover. Login and photo are separate actions on the list. */
@Component({
  selector: 'app-driver-form-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, InputComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal [title]="isEdit() ? 'Edit Driver' : 'Add Driver'" [dismissible]="!saving()" (closed)="closed.emit()">
      @if (loading()) {
        <p class="hint">Loading…</p>
      } @else {
        <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div class="grid">
            <app-input label="Full name" [required]="true" formControlName="fullName" [errorText]="error('fullName')" />
            <app-input label="Mobile number" type="tel" [required]="true" placeholder="10 digits" formControlName="mobileNumber" [errorText]="error('mobileNumber')" />
            <app-input label="Email" type="email" placeholder="Optional" helpText="Login details are emailed here when you create a login." formControlName="email" [errorText]="error('email')" />
            <app-input
              label="Driving licence number"
              [required]="true"
              helpText="Letters, digits, / and -, 5 to 30 characters."
              formControlName="dlNumber"
              [errorText]="error('dlNumber')"
            />
            <app-input label="Licence valid upto" type="date" [required]="true" formControlName="dlValidUpto" [errorText]="error('dlValidUpto')" />
            <app-select label="Vehicle" placeholder="Not assigned" [options]="vehicleOptions()" formControlName="vehicleId" />
          </div>
          @if (inactiveVehicleNote(); as note) {
            <p class="note">{{ note }}</p>
          }
          <app-textarea label="Address" [required]="true" [maxLength]="500" formControlName="address" [errorText]="error('address')" />
          <app-select label="Areas covered" [multiple]="true" placeholder="Optional" [options]="areaOptions()" formControlName="areaIds" />
          @if (droppedAreas().length > 0) {
            <p class="note">
              These areas are inactive and will be removed from this driver when you save: {{ droppedAreas().join(', ') }}.
            </p>
          }
          <!-- lets Enter submit; the visible button lives in the modal footer -->
          <button type="submit" hidden tabindex="-1"></button>
        </form>
      }

      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button [disabled]="saving() || loading()" (click)="submit()">{{ saving() ? 'Saving…' : isEdit() ? 'Update' : 'Create' }}</app-button>
      </ng-container>
    </app-modal>
  `,
  styles: `
    .form {
      display: flex;
      flex-direction: column;
      gap: var(--space-5);
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--space-5);
    }
    @media (max-width: 640px) {
      .grid {
        grid-template-columns: 1fr;
      }
    }
    .hint,
    .note {
      margin: 0;
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }
  `,
})
export class DriverFormModalComponent implements OnInit {
  private readonly service = inject(TransportService);
  private readonly toast = inject(ToastService);

  /** The driver being edited (loaded in full by this dialog), or null to create. */
  readonly driverId = input<string | null>(null);
  /** Active vehicles and areas, as the server returned them. */
  readonly lookups = input.required<DriverLookups>();
  readonly saved = output<Driver>();
  readonly closed = output<void>();

  protected readonly isEdit = computed(() => this.driverId() !== null);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);
  private readonly current = signal<Driver | null>(null);

  protected readonly vehicleOptions = computed<SelectOption[]>(() => [{ label: 'Not assigned', value: '' }, ...this.lookups().vehicles.map((v) => ({ label: v.name, value: String(v.id) }))]);
  protected readonly areaOptions = computed<SelectOption[]>(() => this.lookups().areas.map((a) => ({ label: a.name, value: String(a.id) })));

  /** Assigned areas that are no longer Active: the server refuses them, so they are dropped from the selection and the user is told. */
  protected readonly droppedAreas = computed(() => (this.current()?.areas ?? []).filter((a) => !a.isActive).map((a) => a.areaName));
  protected readonly inactiveVehicleNote = computed(() => {
    const d = this.current();
    if (!d?.vehicleId || this.lookups().vehicles.some((v) => v.id === d.vehicleId)) return null;
    return `The assigned vehicle ${d.vehicleLabel ?? ''} is inactive, so it is not offered here. Pick another vehicle, or leave it unassigned, then save.`;
  });

  protected readonly form = new FormGroup({
    fullName: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(150)] }),
    mobileNumber: new FormControl('', { nonNullable: true, validators: [Validators.required, mobile] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.email, Validators.maxLength(150)] }),
    address: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(500)] }),
    dlNumber: new FormControl('', { nonNullable: true, validators: [Validators.required, dl] }),
    dlValidUpto: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    vehicleId: new FormControl('', { nonNullable: true }),
    areaIds: new FormControl<string[]>([], { nonNullable: true }),
  });

  ngOnInit(): void {
    const id = this.driverId();
    if (!id) return;
    this.loading.set(true);
    this.service
      .getDriver(id)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (d) => {
          this.current.set(d);
          const activeVehicle = this.lookups().vehicles.some((v) => v.id === d.vehicleId);
          this.form.patchValue({
            fullName: d.fullName,
            mobileNumber: d.mobileNumber,
            email: d.email ?? '',
            address: d.address,
            dlNumber: d.dlNumber,
            dlValidUpto: d.dlValidUpto,
            vehicleId: d.vehicleId != null && activeVehicle ? String(d.vehicleId) : '',
            areaIds: d.areas.filter((a) => a.isActive).map((a) => String(a.areaId)),
          });
        },
        error: () => this.closed.emit(), // already toasted by the interceptor
      });
  }

  protected error(name: Field): string | undefined {
    const c = this.form.controls[name];
    if (!(c.touched || this.submitted())) return undefined;
    if (c.hasError('required') || c.hasError('blank')) return 'This field is required';
    if (c.hasError('maxlength')) return `Maximum ${c.getError('maxlength').requiredLength} characters`;
    if (c.hasError('email')) return 'Enter a valid email address';
    if (c.hasError('mobile')) return 'Enter a valid 10-digit mobile number';
    if (c.hasError('dl')) return 'Use 5 to 30 capital letters, digits, / or -';
    return undefined;
  }

  protected submit(): void {
    if (this.saving() || this.loading()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    const v = this.form.getRawValue();
    const body = {
      fullName: v.fullName.trim(),
      mobileNumber: v.mobileNumber.trim(),
      email: v.email.trim() || null,
      address: v.address.trim(),
      dlNumber: normalizeCode(v.dlNumber),
      dlValidUpto: v.dlValidUpto,
      vehicleId: v.vehicleId ? Number(v.vehicleId) : null,
      areaIds: v.areaIds.map(Number),
    };
    const id = this.driverId();

    this.saving.set(true);
    (id ? this.service.updateDriver(id, body) : this.service.createDriver(body))
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (saved) => {
          this.toast.success(`Driver ${id ? 'updated' : 'created'} successfully.`);
          this.saved.emit(saved);
        },
        // Already toasted by the interceptor (e.g. 409 for a duplicate licence or mobile); the modal stays open.
        error: () => undefined,
      });
  }
}

function notBlank(control: AbstractControl): ValidationErrors | null {
  return String(control.value ?? '').trim() === '' ? { blank: true } : null;
}

function mobile(control: AbstractControl): ValidationErrors | null {
  const raw = String(control.value ?? '').trim();
  return raw === '' || MOBILE.test(raw) ? null : { mobile: true };
}

function dl(control: AbstractControl): ValidationErrors | null {
  const raw = normalizeCode(control.value);
  return raw === '' || DL.test(raw) ? null : { dl: true };
}
