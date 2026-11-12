import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../core/services/toast.service';
import { ButtonComponent, IconComponent, ModalComponent, TextareaComponent } from '../../../shared/components';
import { PasswordResetResult, PasswordResetService, RESET_REASON_MAX } from '../password-reset.service';

/**
 * Reset a login's password. Step 1 is the confirmation: what will happen, and a required reason (it is kept in the audit log). Step 2 shows the new
 * temporary password ONCE with a copy button; closing the dialog drops it from memory, and the server cannot show it again.
 */
@Component({
  selector: 'app-reset-password-dialog',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, IconComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './reset-password-dialog.component.scss',
  template: `
    @if (result(); as r) {
      <app-modal title="Password reset" [dismissible]="false">
        <div class="warning" role="alert">
          <app-icon name="triangle-alert" [size]="18" />
          <span>
            <strong>Copy these credentials now.</strong> The temporary password is shown only once and cannot be viewed again.
            {{ r.displayName }} has been signed out everywhere and must change it the next time they sign in.
          </span>
        </div>

        <dl class="credentials">
          <div class="row">
            <dt>Login ID</dt>
            <dd><code>{{ r.loginId }}</code></dd>
            <app-button variant="secondary" size="sm" aria-label="Copy login ID" (click)="copy(r.loginId, 'Login ID')"><app-icon name="copy" [size]="14" />Copy</app-button>
          </div>
          <div class="row">
            <dt>Temporary password</dt>
            <dd><code>{{ r.temporaryPassword }}</code></dd>
            <app-button variant="secondary" size="sm" aria-label="Copy password" (click)="copy(r.temporaryPassword, 'Password')"><app-icon name="copy" [size]="14" />Copy</app-button>
          </div>
        </dl>
        <p class="hint">No email is sent. Give the password to {{ r.displayName }} yourself.</p>

        <ng-container modal-footer>
          <app-button (click)="finish()">Done</app-button>
        </ng-container>
      </app-modal>
    } @else {
      <app-modal title="Reset password?" [dismissible]="!saving()" (closed)="closed.emit()">
        <p>
          This sets a new random temporary password for <strong>{{ displayName() }}</strong> (<code>{{ loginId() }}</code>).
          The old password stops working immediately, every session they have is signed out, and they must choose a new password the next time they sign in.
        </p>
        <app-textarea
          label="Reason"
          [required]="true"
          [rows]="3"
          [maxLength]="maxReason"
          helpText="Kept in the audit log with your name and the time (never the password)."
          placeholder="For example: forgot the password, or the account may be compromised"
          [formControl]="reason"
          [errorText]="showError() ? 'A reason is required.' : undefined"
        />

        <ng-container modal-footer>
          <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
          <app-button variant="danger" [disabled]="saving()" (click)="submit()">{{ saving() ? 'Resetting…' : 'Reset password' }}</app-button>
        </ng-container>
      </app-modal>
    }
  `,
})
export class ResetPasswordDialogComponent {
  private readonly service = inject(PasswordResetService);
  private readonly toast = inject(ToastService);

  /** The target's login id (employee id, student id or school id) and the name shown to the admin. */
  readonly loginId = input.required<string>();
  readonly displayName = input<string>('');

  /** Emitted when the admin is done with the credentials (the result is never passed on). */
  readonly done = output<void>();
  readonly closed = output<void>();

  protected readonly maxReason = RESET_REASON_MAX;
  protected readonly reason = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(RESET_REASON_MAX)] });
  protected readonly saving = signal(false);
  protected readonly attempted = signal(false);
  protected readonly result = signal<PasswordResetResult | null>(null);

  protected showError(): boolean {
    return this.attempted() && this.reason.value.trim() === '';
  }

  protected submit(): void {
    if (this.saving()) return;
    this.attempted.set(true);
    if (this.reason.value.trim() === '' || this.reason.invalid) return;
    this.saving.set(true);
    this.service
      .reset(this.loginId(), this.reason.value)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (r) => this.result.set(r),
        error: () => undefined, // the server's reason (not allowed, inactive login, ...) is toasted by the interceptor
      });
  }

  protected finish(): void {
    this.result.set(null); // drop the password from memory
    this.done.emit();
  }

  protected async copy(value: string, label: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(value);
      this.toast.success(`${label} copied to clipboard.`);
    } catch {
      this.toast.warning('Could not copy automatically — please select and copy it manually.');
    }
  }
}
