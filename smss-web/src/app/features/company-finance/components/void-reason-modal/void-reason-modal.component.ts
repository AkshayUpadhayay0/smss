import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonComponent, ModalComponent, TextareaComponent } from '../../../../shared/components';

/** Asks for the reason a record is being voided (required, never blank). The caller makes the API call and says when it is `busy`. */
@Component({
  selector: 'app-void-reason-modal',
  imports: [ReactiveFormsModule, ButtonComponent, ModalComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `.note { margin: 0 0 var(--space-4); font-size: var(--text-sm); color: var(--color-text-muted); }`,
  template: `
    <app-modal [title]="title()" [dismissible]="!busy()" (closed)="closed.emit()">
      <form id="void-reason-form" (ngSubmit)="submit()" novalidate>
        <p class="note">{{ prompt() }}</p>
        <app-textarea label="Reason for voiding" [required]="true" [rows]="3" [maxLength]="500" [formControl]="reason" [errorText]="submitted() && reason.invalid ? 'A reason is required' : undefined" />
      </form>
      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="busy()" (click)="closed.emit()">Cancel</app-button>
        <app-button variant="danger" [disabled]="busy()" (click)="submit()">{{ busy() ? 'Voiding…' : 'Void' }}</app-button>
      </ng-container>
    </app-modal>
  `,
})
export class VoidReasonModalComponent {
  readonly title = input('Void');
  readonly prompt = input('It stays on record, marked Voided, and no longer counts in any total.');
  readonly busy = input(false);
  readonly confirmed = output<string>();
  readonly closed = output<void>();

  protected readonly submitted = signal(false);
  protected readonly reason = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(500)] });

  protected submit(): void {
    if (this.busy()) return;
    this.submitted.set(true);
    const text = this.reason.value.trim();
    if (!text) return;
    this.confirmed.emit(text);
  }
}
