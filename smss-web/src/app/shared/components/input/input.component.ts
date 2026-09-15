import { ChangeDetectionStrategy, Component, forwardRef, input, signal } from '@angular/core';
import { NG_VALUE_ACCESSOR, ControlValueAccessor } from '@angular/forms';
import { IconComponent } from '../icon/icon.component';

let uid = 0;

/**
 * Reusable text-field ControlValueAccessor. Works with Reactive Forms
 * (formControlName / formControl) exactly like a native <input>, but adds
 * label/help/error/prefix/suffix chrome so every page renders fields the
 * same way (spec #9, #28).
 */
@Component({
  selector: 'app-input',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './input.component.html',
  styleUrl: './input.component.scss',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => InputComponent),
      multi: true,
    },
  ],
})
export class InputComponent implements ControlValueAccessor {
  readonly label = input<string | undefined>(undefined);
  readonly type = input<string>('text');
  readonly placeholder = input<string>('');
  readonly help = input<string | undefined>(undefined);
  readonly errorText = input<string | undefined>(undefined);
  readonly required = input<boolean>(false);
  readonly readonly = input<boolean>(false);
  readonly prefixIcon = input<string | undefined>(undefined);
  readonly showPasswordToggle = input<boolean>(false);

  readonly inputId = `app-input-${++uid}`;
  readonly value = signal<string>('');
  readonly disabledState = signal(false);
  readonly showPassword = signal(false);

  protected onChange: (value: string) => void = () => {};
  protected onTouched: () => void = () => {};

  get resolvedType(): string {
    if (this.type() === 'password' && this.showPasswordToggle()) {
      return this.showPassword() ? 'text' : 'password';
    }
    return this.type();
  }

  writeValue(value: string): void {
    this.value.set(value ?? '');
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabledState.set(isDisabled);
  }

  handleInput(event: Event): void {
    const target = event.target as HTMLInputElement;
    this.value.set(target.value);
    this.onChange(target.value);
  }

  togglePasswordVisibility(): void {
    this.showPassword.update((v) => !v);
  }
}
