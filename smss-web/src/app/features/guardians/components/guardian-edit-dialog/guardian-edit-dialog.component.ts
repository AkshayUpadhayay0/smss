import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { whatsAppValidator } from '../../../../core/utils/whatsapp';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, WhatsAppFieldComponent } from '../../../../shared/components';
import { firstErrorMessage, mobileValidator, requiredTrimmed } from '../../../schools/utils/school-validators';
import { Guardian } from '../../models/guardian.model';
import { GuardianService } from '../../services/guardian.service';

/** A WhatsApp number may be left blank unless this guardian is the primary guardian of an active student; a number that is typed must be valid. */
const optionalWhatsApp = (c: AbstractControl): ValidationErrors | null => (String(c.value ?? '').trim() === '' ? null : whatsAppValidator(c));

/** An email is checked after trimming (the server trims it too), so a stray space from a paste does not block saving. */
const trimmedEmail = (c: AbstractControl): ValidationErrors | null => Validators.email(new FormControl(String(c.value ?? '').trim()));

/**
 * Edit one guardian: name, mobile, WhatsApp (with the "same as mobile number" tick), email and occupation. The primary guardian of an active student cannot be saved without WhatsApp and email
 * (the server checks it too). When the guardian is linked to several students the form says so: the change applies to all of them. Changes to mobile, WhatsApp and email are logged by the server.
 */
@Component({
  selector: 'app-guardian-edit-dialog',
  imports: [ReactiveFormsModule, ButtonComponent, InputComponent, ModalComponent, WhatsAppFieldComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal [title]="'Edit parent details'" [dismissible]="!saving()" (closed)="closed.emit()">
      @if (loadFailed()) {
        <p class="note" role="alert">The guardian could not be loaded.</p>
      } @else if (!guardian()) {
        <p class="note" role="status">Loading…</p>
      } @else {
        @if (guardian()!.linkedStudentCount > 1) {
          <p class="shared" data-note="shared" role="status">
            Applies to {{ guardian()!.linkedStudentCount }} students: {{ studentNames() }}. A change made here is seen on all of them.
          </p>
        }
        <form [formGroup]="form" class="grid" (ngSubmit)="save()" novalidate>
          <app-input label="Name" [required]="true" formControlName="name" [errorText]="err('name')" />
          <app-input label="Mobile number" type="tel" [required]="true" placeholder="10-digit mobile" formControlName="mobileNumber" [errorText]="err('mobileNumber')" />
          <app-whatsapp-field label="WhatsApp number" [required]="guardian()!.contactRequired" [number]="form.controls.whatsAppNumber" [same]="form.controls.whatsAppSameAsMobile" [error]="err('whatsAppNumber')" />
          <app-input label="Email" type="email" [required]="guardian()!.contactRequired" formControlName="email" [errorText]="err('email')" />
          <app-input label="Occupation" placeholder="Optional" formControlName="occupation" [errorText]="err('occupation')" />
        </form>
        @if (guardian()!.contactRequired) {
          <p class="hint" data-note="required">WhatsApp and email are required: this is the primary guardian of a student.</p>
        }
      }
      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button [disabled]="saving() || !guardian()" (click)="save()">{{ saving() ? 'Saving…' : 'Save' }}</app-button>
      </ng-container>
    </app-modal>
  `,
  styles: `
    .grid { display: grid; gap: var(--space-4); }
    .shared { margin: 0 0 var(--space-4); padding: var(--space-3); border-radius: var(--radius-md); background: #eff6ff; color: #1e40af; font-size: var(--text-sm); }
    .hint, .note { font-size: var(--text-sm); color: var(--color-text-muted); }
  `,
})
export class GuardianEditDialogComponent implements OnInit {
  private readonly service = inject(GuardianService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly guardianId = input.required<number>();
  readonly saved = output<Guardian>();
  readonly closed = output<void>();

  protected readonly guardian = signal<Guardian | null>(null);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [requiredTrimmed, Validators.maxLength(100)] }),
    mobileNumber: new FormControl('', { nonNullable: true, validators: [mobileValidator] }),
    whatsAppNumber: new FormControl('', { nonNullable: true, validators: [optionalWhatsApp] }),
    whatsAppSameAsMobile: new FormControl(false, { nonNullable: true }),
    email: new FormControl('', { nonNullable: true, validators: [trimmedEmail, Validators.maxLength(150)] }),
    occupation: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(150)] }),
  });

  protected studentNames(): string {
    return (this.guardian()?.students ?? []).map((s) => s.studentName).join(', ');
  }

  ngOnInit(): void {
    this.service
      .get(this.guardianId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (g) => {
          this.guardian.set(g);
          if (g.contactRequired) {
            this.form.controls.email.addValidators(requiredTrimmed);
            // a required WhatsApp: the number, or the tick (the field component switches the number box off when the tick is on)
            this.form.controls.whatsAppNumber.setValidators([whatsAppValidator]);
          }
          this.form.patchValue({ name: g.name, mobileNumber: g.mobileNumber, whatsAppNumber: g.whatsAppNumber ?? '', whatsAppSameAsMobile: false, email: g.email ?? '', occupation: g.occupation ?? '' });
          this.form.controls.email.updateValueAndValidity();
          this.form.controls.whatsAppNumber.updateValueAndValidity();
        },
        error: () => this.loadFailed.set(true),
      });
  }

  protected err(name: keyof typeof this.form.controls): string | undefined {
    const control = this.form.controls[name];
    return firstErrorMessage(control, this.submitted());
  }

  protected save(): void {
    if (this.saving() || !this.guardian()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    this.saving.set(true);
    this.service
      .update(this.guardianId(), {
        name: v.name.trim(),
        mobileNumber: v.mobileNumber.trim(),
        whatsAppNumber: v.whatsAppSameAsMobile ? null : v.whatsAppNumber.trim() || null,
        whatsAppSameAsMobile: v.whatsAppSameAsMobile,
        email: v.email.trim() || null,
        occupation: v.occupation.trim() || null,
      })
      .pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (g) => {
          this.toast.success('Parent details saved.');
          this.saved.emit(g);
        },
        error: () => undefined,   // the server reason is toasted by the interceptor; the form keeps its values
      });
  }
}
