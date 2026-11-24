import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, input } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { InputComponent } from '../input/input.component';

/**
 * A WhatsApp number box with a "same as mobile number" tick. Ticking it switches the box off (the server then uses the mobile number as the WhatsApp number); unticking it
 * switches the box back on. The two controls live in the caller's form; this only draws them and keeps them in step.
 */
@Component({
  selector: 'app-whatsapp-field',
  imports: [ReactiveFormsModule, InputComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="wa">
      <app-input [label]="label()" type="tel" [required]="true" placeholder="10 digits or +91 98765 43210" [formControl]="number()" [errorText]="error()" />
      <label class="same"><input type="checkbox" [formControl]="same()" data-field="whatsapp-same" /> <span>Same as mobile number</span></label>
    </div>
  `,
  styles: `
    .wa {
      display: flex;
      flex-direction: column;
      gap: var(--space-2, 0.5rem);
    }
    .same {
      display: inline-flex;
      align-items: center;
      gap: var(--space-2, 0.5rem);
      font-size: 0.875rem;
      cursor: pointer;
    }
  `,
})
export class WhatsAppFieldComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  readonly label = input('WhatsApp number');
  readonly number = input.required<FormControl<string>>();
  readonly same = input.required<FormControl<boolean>>();
  readonly error = input<string | undefined>();

  ngOnInit(): void {
    const apply = (on: boolean) => {
      if (this.same().disabled) return;   // a read-only (view) form: leave both boxes exactly as the form put them
      if (on) { this.number().setValue('', { emitEvent: false }); this.number().disable({ emitEvent: false }); }
      else this.number().enable({ emitEvent: false });
      this.number().updateValueAndValidity();
    };
    apply(this.same().value);
    this.same().valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(apply);
  }
}
