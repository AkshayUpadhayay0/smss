import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent, InputComponent, ModalComponent, SelectComponent, TextareaComponent } from '../../../../shared/components';
import { MasterConfig, MasterItem } from '../../models/master.model';
import { MasterCrudService } from '../../services/master-crud.service';

/** Add / edit modal driven entirely by `config.fields`. Saves itself; the parent reacts to `saved`. */
@Component({
  selector: 'app-master-form-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, IconComponent, InputComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './master-form-modal.component.scss',
  templateUrl: './master-form-modal.component.html',
})
export class MasterFormModalComponent implements OnInit {
  private readonly crud = inject(MasterCrudService);
  private readonly toast = inject(ToastService);

  readonly config = input.required<MasterConfig>();
  /** The record being edited, or null to create. */
  readonly item = input<MasterItem | null>(null);

  readonly saved = output<MasterItem>();
  readonly closed = output<void>();

  protected readonly isEdit = computed(() => this.item() !== null);
  protected readonly title = computed(() => `${this.isEdit() ? 'Edit' : 'Add'} ${this.config().title}`);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected form = new FormGroup<Record<string, FormControl<string>>>({});

  ngOnInit(): void {
    const item = this.item();
    const controls: Record<string, FormControl<string>> = {};
    for (const f of this.config().fields) {
      const validators: ValidatorFn[] = [];
      if (f.required) validators.push(Validators.required, notBlank);
      if (f.minLength) validators.push(Validators.minLength(f.minLength));
      if (f.maxLength) validators.push(Validators.maxLength(f.maxLength));
      const initial = item?.[f.key];
      controls[f.key] = new FormControl(initial == null ? '' : String(initial), { nonNullable: true, validators });
      if (item && f.createOnly) controls[f.key].disable(); // immutable once created
    }
    this.form = new FormGroup(controls);
  }

  protected error(key: string): string | undefined {
    const c = this.form.controls[key];
    if (!c || c.valid || c.disabled || !(c.touched || this.submitted())) return undefined;
    if (c.hasError('required') || c.hasError('blank')) return 'This field is required';
    if (c.hasError('minlength')) return `Minimum ${c.getError('minlength').requiredLength} characters`;
    if (c.hasError('maxlength')) return `Maximum ${c.getError('maxlength').requiredLength} characters`;
    return 'Invalid value';
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    const cfg = this.config();
    const editing = this.item();
    const body: Record<string, unknown> = {};
    for (const f of cfg.fields) {
      if (editing && f.createOnly) continue; // not part of the update DTO
      const value = this.form.controls[f.key].value.trim();
      body[f.key] = value === '' ? null : value; // blank optional fields go out as null
    }

    this.saving.set(true);
    const request$ = editing
      ? this.crud.update(cfg.path, editing[cfg.idKey] as number, body)
      : this.crud.create(cfg.path, body);
    request$.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (saved) => {
        this.toast.success(`${cfg.title} ${editing ? 'updated' : 'created'} successfully.`);
        this.saved.emit(saved);
      },
      // Already toasted by the interceptor; the modal stays open so nothing typed is lost.
      error: () => undefined,
    });
  }
}

function notBlank(control: { value: unknown }): { blank: true } | null {
  return String(control.value ?? '').trim() === '' ? { blank: true } : null;
}
