import { ChangeDetectionStrategy, Component, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

let nextId = 0;

@Component({
  selector: 'app-textarea',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './textarea.component.scss',
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => TextareaComponent), multi: true }],
  template: `
    <div class="field">
      @if (label()) {
        <label [for]="id">{{ label() }}@if (required()) {<span class="required">*</span>}</label>
      }
      <div class="control" [class.invalid]="!!errorText()" [class.disabled]="isDisabled()">
        <textarea
          [id]="id"
          [rows]="rows()"
          [value]="value()"
          [placeholder]="placeholder()"
          [attr.maxlength]="maxLength()"
          [readOnly]="readonly()"
          [disabled]="isDisabled()"
          [attr.aria-invalid]="!!errorText()"
          (input)="onInput($event)"
          (blur)="onTouched()"
        ></textarea>
      </div>
      @if (errorText()) {
        <span class="error">{{ errorText() }}</span>
      } @else if (helpText()) {
        <span class="help">{{ helpText() }}</span>
      }
    </div>
  `,
})
export class TextareaComponent implements ControlValueAccessor {
  protected readonly id = `app-textarea-${nextId++}`;

  readonly label = input<string>();
  readonly required = input(false);
  readonly placeholder = input('');
  readonly rows = input(3);
  readonly maxLength = input<number>();
  readonly helpText = input<string>();
  readonly errorText = input<string>();
  readonly readonly = input(false);
  readonly disabled = input(false);

  protected readonly value = signal('');
  private readonly formDisabled = signal(false);
  protected isDisabled(): boolean {
    return this.disabled() || this.formDisabled();
  }

  private onChange: (v: string) => void = () => {};
  protected onTouched: () => void = () => {};

  protected onInput(event: Event): void {
    const v = (event.target as HTMLTextAreaElement).value;
    this.value.set(v);
    this.onChange(v);
  }

  writeValue(v: string | null): void {
    this.value.set(v ?? '');
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
