import { ChangeDetectionStrategy, Component, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { IconComponent } from '../icon/icon.component';
import { IconName } from '../icon/icons';

let nextId = 0;

@Component({
  selector: 'app-input',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './input.component.scss',
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => InputComponent), multi: true }],
  template: `
    <div class="field">
      @if (label()) {
        <label [for]="id">{{ label() }}@if (required()) {<span class="required">*</span>}</label>
      }
      <div class="control" [class.invalid]="!!errorText()" [class.disabled]="isDisabled()">
        @if (prefixIcon()) {
          <app-icon [name]="prefixIcon()!" [size]="16" />
        }
        <input
          [id]="id"
          [type]="type()"
          [value]="value()"
          [placeholder]="placeholder()"
          [readOnly]="readonly()"
          [disabled]="isDisabled()"
          [attr.aria-invalid]="!!errorText()"
          (input)="onInput($event)"
          (blur)="onTouched()"
        />
      </div>
      @if (errorText()) {
        <span class="error">{{ errorText() }}</span>
      } @else if (helpText()) {
        <span class="help">{{ helpText() }}</span>
      }
    </div>
  `,
})
export class InputComponent implements ControlValueAccessor {
  protected readonly id = `app-input-${nextId++}`;

  readonly label = input<string>();
  readonly type = input<'text' | 'email' | 'password' | 'number' | 'tel' | 'date' | 'url'>('text');
  readonly required = input(false);
  readonly placeholder = input('');
  readonly prefixIcon = input<IconName>();
  readonly helpText = input<string>();
  readonly errorText = input<string>();
  readonly readonly = input(false);
  /** Standalone use; with forms the control's disabled state is applied via setDisabledState. */
  readonly disabled = input(false);

  protected readonly value = signal('');
  private readonly formDisabled = signal(false);
  protected isDisabled(): boolean {
    return this.disabled() || this.formDisabled();
  }

  private onChange: (v: string) => void = () => {};
  protected onTouched: () => void = () => {};

  protected onInput(event: Event): void {
    const v = (event.target as HTMLInputElement).value;
    this.value.set(v);
    this.onChange(v);
  }

  writeValue(v: string | number | null): void {
    this.value.set(v == null ? '' : String(v));
  }
  registerOnChange(fn: (v: string) => void): void {
    this.onChange = fn;
  }
  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }
  setDisabledState(isDisabled: boolean): void {
    this.formDisabled.set(isDisabled);
  }
}
