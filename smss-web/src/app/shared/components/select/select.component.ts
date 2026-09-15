import { ChangeDetectionStrategy, Component, forwardRef, input, signal } from '@angular/core';
import { NG_VALUE_ACCESSOR, ControlValueAccessor } from '@angular/forms';

export interface SelectOption {
  label: string;
  value: string;
}

let uid = 0;

@Component({
  selector: 'app-select',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './select.component.html',
  styleUrl: './select.component.scss',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => SelectComponent),
      multi: true,
    },
  ],
})
export class SelectComponent implements ControlValueAccessor {
  readonly label = input<string | undefined>(undefined);
  readonly options = input.required<SelectOption[]>();
  readonly placeholder = input<string>('Select an option');
  readonly help = input<string | undefined>(undefined);
  readonly errorText = input<string | undefined>(undefined);
  readonly required = input<boolean>(false);
  readonly multiple = input<boolean>(false);

  readonly selectId = `app-select-${++uid}`;
  readonly value = signal<string>('');
  readonly multiValue = signal<string[]>([]);
  readonly disabledState = signal(false);

  protected onChange: (value: string | string[]) => void = () => {};
  protected onTouched: () => void = () => {};

  writeValue(value: string | string[]): void {
    if (this.multiple()) {
      this.multiValue.set(Array.isArray(value) ? value : []);
    } else {
      this.value.set((value as string) ?? '');
    }
  }

  registerOnChange(fn: (value: string | string[]) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabledState.set(isDisabled);
  }

  handleChange(event: Event): void {
    const target = event.target as HTMLSelectElement;
    if (this.multiple()) {
      const selected = Array.from(target.selectedOptions).map((o) => o.value);
      this.multiValue.set(selected);
      this.onChange(selected);
    } else {
      this.value.set(target.value);
      this.onChange(target.value);
    }
  }
}
