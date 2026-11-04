import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { HEX_COLOR } from '../../models/id-card-settings.model';

let nextId = 0;

/** A brand colour: the native colour picker, a hex box and a "template default" reset. Emits only valid #rrggbb values. */
@Component({
  selector: 'app-color-field',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './color-field.component.scss',
  template: `
    <div class="field">
      <label [for]="id">{{ label() }}</label>
      <div class="control" [class.invalid]="invalid()">
        <input class="swatch" type="color" [id]="id" [value]="safeValue()" [attr.aria-label]="label() + ' picker'" (input)="pick($event)" />
        <input
          class="hex"
          type="text"
          maxlength="7"
          spellcheck="false"
          autocomplete="off"
          [value]="value()"
          [attr.aria-label]="label() + ' hex code'"
          [attr.aria-invalid]="invalid()"
          (input)="typed($event)"
        />
      </div>
      @if (invalid()) {
        <span class="error">Use a hex colour like {{ defaultValue() }}</span>
      } @else if (value().toUpperCase() !== defaultValue().toUpperCase()) {
        <button type="button" class="reset" (click)="valueChange.emit(defaultValue())">Use template default ({{ defaultValue() }})</button>
      }
    </div>
  `,
})
export class ColorFieldComponent {
  protected readonly id = `app-color-${nextId++}`;

  readonly label = input.required<string>();
  readonly value = input.required<string>();
  /** The template's own colour, offered as a reset. */
  readonly defaultValue = input.required<string>();
  /** Fires for every valid colour (a half-typed hex is held back). */
  readonly valueChange = output<string>();

  protected readonly invalid = computed(() => !HEX_COLOR.test(this.value()));
  protected readonly safeValue = computed(() => (HEX_COLOR.test(this.value()) ? this.value().toLowerCase() : '#000000'));

  protected pick(event: Event): void {
    this.valueChange.emit((event.target as HTMLInputElement).value.toUpperCase());
  }

  protected typed(event: Event): void {
    let v = (event.target as HTMLInputElement).value.trim();
    if (v && !v.startsWith('#')) v = `#${v}`;
    if (HEX_COLOR.test(v)) this.valueChange.emit(v.toUpperCase());
  }
}
