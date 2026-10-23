import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonComponent, ModalComponent, TextareaComponent } from '../../../../shared/components';

/** Rejecting needs a reason (the server insists too). Emits the trimmed reason; the page sends the decision. */
@Component({
  selector: 'app-leave-reject-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal title="Reject leave request" [dismissible]="!busy()" (closed)="closed.emit()">
      <p class="who">{{ summary() }}</p>
      <app-textarea label="Reason for rejecting" [required]="true" placeholder="The applicant will see this" [formControl]="reason" [errorText]="shown() ? error() : undefined" />

      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="busy()" (click)="closed.emit()">Cancel</app-button>
        <app-button variant="danger" [disabled]="busy()" (click)="submit()">{{ busy() ? 'Rejecting…' : 'Reject' }}</app-button>
      </ng-container>
    </app-modal>
  `,
  styles: `
    .who {
      margin: 0 0 var(--space-4);
      color: var(--color-text-muted);
    }
  `,
})
export class LeaveRejectModalComponent {
  /** One line naming the request, e.g. "Anita Staff · Casual Leave · 10 Oct – 12 Oct 2026". */
  readonly summary = input('');
  readonly busy = input(false);
  readonly confirmed = output<string>();
  readonly closed = output<void>();

  protected readonly reason = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(500)] });
  protected readonly shown = signal(false);

  protected error(): string | undefined {
    if (this.reason.value.trim() === '') return 'A reason is required';
    if (this.reason.hasError('maxlength')) return 'Maximum 500 characters';
    return undefined;
  }

  protected submit(): void {
    this.shown.set(true);
    if (this.error()) return;
    this.confirmed.emit(this.reason.value.trim());
  }
}
