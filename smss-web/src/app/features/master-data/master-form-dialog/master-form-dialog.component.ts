import { Component, EventEmitter, Input, OnInit, Output, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MasterConfig, MasterItem } from '../../../core/models/master-data.model';
import { AlertComponent } from '../../../shared/components/alert/alert.component';
import { ButtonComponent } from '../../../shared/components/button/button.component';
import { InputComponent } from '../../../shared/components/input/input.component';
import { ModalComponent } from '../../../shared/components/modal/modal.component';
import { TextareaComponent } from '../../../shared/components/textarea/textarea.component';

type FieldName = 'code' | 'name' | 'type' | 'description';

@Component({
  selector: 'app-master-form-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, ModalComponent, AlertComponent, InputComponent, TextareaComponent, ButtonComponent],
  templateUrl: './master-form-dialog.component.html',
  styleUrls: ['./master-form-dialog.component.scss']
})
export class MasterFormDialogComponent implements OnInit {
  @Input({ required: true }) config!: MasterConfig;
  @Input() item: MasterItem | null = null;      // null = create mode
  @Input() saving = false;
  @Input() errorMessage = '';

  @Output() save = new EventEmitter<Record<string, unknown>>();
  @Output() cancel = new EventEmitter<void>();

  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.group({
    code: ['', [Validators.required, Validators.minLength(2), Validators.pattern(/^[A-Za-z0-9_]+$/)]],
    name: ['', [Validators.required, Validators.minLength(2)]],
    type: [''],
    description: ['']
  });

  get isEdit(): boolean { return this.item !== null; }
  get hasCode(): boolean { return !!this.config.keys.code; }
  get hasDescription(): boolean { return this.config.hasDescription !== false; }
  get typeField() { return this.config.typeField; }
  get codeLabel(): string { return this.config.codeLabel ?? 'Code'; }
  get dialogTitle(): string { return `${this.isEdit ? 'Edit' : 'Add'} ${this.config.singular}`; }

  /** Inline validation message for a field, shown once it has been touched. */
  errorFor(field: FieldName, label: string): string | undefined {
    const ctrl = this.form.controls[field];
    if (!ctrl.touched || !ctrl.errors) return undefined;
    const e = ctrl.errors;
    if (e['required']) return `${label} is required.`;
    if (e['minlength']) return `${label} must be at least ${e['minlength'].requiredLength} characters.`;
    if (e['maxlength']) return `${label} must be at most ${e['maxlength'].requiredLength} characters.`;
    if (e['pattern']) return 'Only letters, numbers and underscore are allowed.';
    return undefined;
  }

  ngOnInit(): void {
    const c = this.form.controls;

    if (this.hasCode) c.code.addValidators(Validators.maxLength(this.config.codeMaxLength ?? 50));
    else c.code.disable();

    c.name.addValidators(Validators.maxLength(this.config.nameMaxLength));

    if (this.typeField) c.type.addValidators([Validators.required, Validators.minLength(2), Validators.maxLength(this.typeField.maxLength)]);
    else c.type.disable();

    if (this.hasDescription) c.description.addValidators(Validators.maxLength(2000));
    else c.description.disable();

    if (this.item) {
      this.form.patchValue({
        code: this.hasCode ? this.item[this.config.keys.code!] : '',
        name: this.item[this.config.keys.name],
        type: this.typeField ? this.item[this.typeField.key] : '',
        description: this.item.description ?? ''
      });
      if (this.hasCode) c.code.disable(); // code is immutable after creation
    }

    Object.values(c).forEach(ctrl => ctrl.updateValueAndValidity());
  }

  submit(): void {
    if (this.form.invalid || this.saving) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const payload: Record<string, unknown> = { [this.config.keys.name]: v.name!.trim() };

    if (this.hasDescription) payload['description'] = v.description?.trim() || null;
    if (this.typeField) payload[this.typeField.key] = v.type!.trim();
    if (!this.isEdit && this.hasCode) payload[this.config.keys.code!] = v.code!.trim();

    this.save.emit(payload);
  }
}