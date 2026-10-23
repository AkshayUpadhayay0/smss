import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Observable, finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, SelectComponent, SelectOption, TextareaComponent } from '../../../../shared/components';
import { LeaveType } from '../../models/leave.model';
import { LeaveService } from '../../services/leave.service';

const notBlank = (c: AbstractControl): ValidationErrors | null => (String(c.value ?? '').trim() === '' ? { blank: true } : null);
const toNotBeforeFrom = (g: AbstractControl): ValidationErrors | null => {
  const from = g.get('fromDate')?.value as string;
  const to = g.get('toDate')?.value as string;
  return from && to && to < from ? { toBeforeFrom: true } : null;
};

/**
 * One leave request form for every case: an employee for themselves (no person picker), a School Admin for an employee, a class teacher / School Admin
 * for a student. The server decides who may do what and its message is toasted by the interceptor, so the modal simply stays open on an error.
 */
@Component({
  selector: 'app-leave-apply-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, InputComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal [title]="title()" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        @if (kind() === 'student' || people()) {
          <app-select
            [label]="kind() === 'student' ? 'Student' : 'Employee'"
            [required]="true"
            placeholder="Select…"
            [options]="people() ?? []"
            formControlName="personId"
            [errorText]="error('personId')"
          />
        }
        <app-select label="Leave type" [required]="true" placeholder="Select type…" [options]="typeOptions()" formControlName="leaveTypeId" [errorText]="error('leaveTypeId')" />
        <div class="dates">
          <app-input label="From" type="date" [required]="true" formControlName="fromDate" [errorText]="error('fromDate')" />
          <app-input label="To" type="date" [required]="true" formControlName="toDate" [errorText]="error('toDate')" />
        </div>
        <app-textarea label="Reason" [required]="true" placeholder="Why is leave needed?" formControlName="reason" [errorText]="error('reason')" />
        <button type="submit" hidden tabindex="-1"></button>
      </form>

      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button [disabled]="saving()" (click)="submit()">{{ saving() ? 'Submitting…' : 'Submit request' }}</app-button>
      </ng-container>
    </app-modal>
  `,
  styles: `
    .form {
      display: flex;
      flex-direction: column;
      gap: var(--space-5);
    }

    .dates {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: var(--space-4);
    }
  `,
})
export class LeaveApplyModalComponent implements OnInit {
  private readonly service = inject(LeaveService);
  private readonly toast = inject(ToastService);

  readonly kind = input.required<'employee' | 'student'>();
  /** Who the request is for. null (employee kind) = the signed-in employee, so no picker is shown. */
  readonly people = input<SelectOption[] | null>(null);
  readonly types = input<LeaveType[]>([]);
  readonly saved = output<void>();
  readonly closed = output<void>();

  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);
  protected readonly typeOptions = computed<SelectOption[]>(() => this.types().map((t) => ({ label: t.typeName, value: String(t.leaveTypeId) })));
  protected readonly title = computed(() => (this.kind() === 'student' ? 'Record Student Leave' : this.people() ? 'Record Employee Leave' : 'Apply for Leave'));

  protected readonly form = new FormGroup(
    {
      personId: new FormControl('', { nonNullable: true }),
      leaveTypeId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
      fromDate: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
      toDate: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
      reason: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(500)] }),
    },
    { validators: [toNotBeforeFrom] },
  );

  ngOnInit(): void {
    if (this.kind() === 'student' || this.people()) this.form.controls.personId.addValidators(Validators.required);
    // the end date follows the start while it is blank or would fall before it
    this.form.controls.fromDate.valueChanges.subscribe((from) => {
      const to = this.form.controls.toDate.value;
      if (!to || to < from) this.form.controls.toDate.setValue(from);
    });
  }

  protected error(name: 'personId' | 'leaveTypeId' | 'fromDate' | 'toDate' | 'reason'): string | undefined {
    const c = this.form.controls[name];
    if (!(c.touched || this.submitted())) return undefined;
    if (c.hasError('required') || c.hasError('blank')) return 'This field is required';
    if (c.hasError('maxlength')) return 'Maximum 500 characters';
    if (name === 'toDate' && this.form.hasError('toBeforeFrom')) return 'The to date can not be before the from date';
    return undefined;
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    const v = this.form.getRawValue();
    const common = { leaveTypeId: Number(v.leaveTypeId), fromDate: v.fromDate, toDate: v.toDate, reason: v.reason.trim() };
    const request$: Observable<unknown> =
      this.kind() === 'student'
        ? this.service.applyStudent({ studentId: v.personId, ...common })
        : this.service.applyEmployee(this.people() ? { employeeId: v.personId, ...common } : common);

    this.saving.set(true);
    request$.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => {
        this.toast.success('Leave request submitted.');
        this.saved.emit();
      },
      error: () => undefined, // already toasted by the interceptor
    });
  }
}
