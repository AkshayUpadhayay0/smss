import { ChangeDetectionStrategy, Component, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { IconComponent } from '../icon/icon.component';

/** Values are always strings — convert numeric ids with `.toString()` when building options. */
export interface SelectOption {
  label: string;
  value: string;
}

let nextId = 0;

@Component({
  selector: 'app-select',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './select.component.scss',
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => SelectComponent), multi: true }],
  template: `
    <div class="field">
      @if (label()) {
        <label [for]="id">{{ label() }}@if (required()) {<span class="required">*</span>}</label>
      }
      <div class="control" [class.invalid]="!!errorText()" [class.disabled]="isDisabled()">
        <select
          [id]="id"
          [multiple]="multiple()"
          [disabled]="isDisabled()"
          [attr.aria-invalid]="!!errorText()"
          (change)="onSelect($event)"
          (blur)="onTouched()"
        >
          @if (!multiple()) {
            <option value="" [selected]="selected().length === 0">{{ placeholder() }}</option>
          }
          @for (opt of options(); track opt.value) {
            <option [value]="opt.value" [selected]="selected().includes(opt.value)">{{ opt.label }}</option>
          }
        </select>
        @if (!multiple()) {
          <app-icon name="chevron-down" [size]="16" />
        }
      </div>
      @if (errorText()) {
        <span class="error">{{ errorText() }}</span>
      }
    </div>
  `,
})
export class SelectComponent implements ControlValueAccessor {
  protected readonly id = `app-select-${nextId++}`;

  readonly label = input<string>();
  readonly options = input<SelectOption[]>([]);
  readonly required = input(false);
  readonly multiple = input(false);
  readonly placeholder = input('Select…');
  readonly errorText = input<string>();
  readonly disabled = input(false);

  protected readonly selected = signal<string[]>([]);
  private readonly formDisabled = signal(false);
  protected isDisabled(): boolean {
    return this.disabled() || this.formDisabled();
  }

  private onChange: (v: string | string[]) => void = () => {};
  protected onTouched: () => void = () => {};

  protected onSelect(event: Event): void {
    const el = event.target as HTMLSelectElement;
    if (this.multiple()) {
      const values = Array.from(el.selectedOptions).map((o) => o.value);
      this.selected.set(values);
      this.onChange(values);
    } else {
      this.selected.set(el.value ? [el.value] : []);
      this.onChange(el.value);
    }
  }

  writeValue(v: string | string[] | null): void {
    this.selected.set(v == null || v === '' ? [] : Array.isArray(v) ? v.map(String) : [String(v)]);
  }
  registerOnChange(fn: (v: string | string[]) => void): void {
    this.onChange = fn;
  }
  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }
  setDisabledState(isDisabled: boolean): void {
    this.formDisabled.set(isDisabled);
  }
}
