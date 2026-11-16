import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonComponent, ModalComponent, TextareaComponent } from '../../../../shared/components';

/**
 * Asks for the written reason a void needs. Shown AFTER the confirm dialog, so every void is confirmed first and then explained.
 * The caller does the request and closes this when it is done (`busy` keeps it open and locked meanwhile).
 */
@Component({
  selector: 'app-void-reason-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../../styles/inventory-page.scss',
  template: `
    <app-modal [title]="title()" [dismissible]="!busy()" (closed)="closed.emit()">
      <form (submit)="$event.preventDefault(); submit()" novalidate>
        <p class="note">{{ message() }}</p>
        <app-textarea
          label="Reason"
          [required]="true"
          [maxLength]="500"
          placeholder="Why is this being voided?"
          [formControl]="reason"
          [errorText]="shown() && reason.invalid ? 'A reason is required' : undefined"
        />
        <button type="submit" hidden tabindex="-1"></button>
      </form>
      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="busy()" (click)="closed.emit()">Cancel</app-button>
        <app-button variant="danger" [disabled]="busy()" (click)="submit()">{{ busy() ? 'Voiding…' : confirmText() }}</app-button>
      </ng-container>
    </app-modal>
  `,
})
export class VoidReasonModalComponent {
  readonly title = input('Void');
  readonly message = input('');
  readonly confirmText = input('Void');
  readonly busy = input(false);
  readonly confirmed = output<string>();
  readonly closed = output<void>();

  protected readonly reason = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(500)] });
  protected readonly shown = signal(false);

  protected submit(): void {
    this.shown.set(true);
    const text = this.reason.value.trim();
    if (!text || this.busy()) return;
    this.confirmed.emit(text);
  }
}
