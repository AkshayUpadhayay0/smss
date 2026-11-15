import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent, ModalComponent } from '../../../../shared/components';
import { DriverLoginCreated } from '../../models/transport.model';
import { TransportService } from '../../services/transport.service';

/**
 * Creates the driver's login (role Driver, no choices to make). Step 1: confirm. Step 2: the generated Login ID and temporary
 * password are shown ONCE (the server cannot return the password again).
 */
@Component({
  selector: 'app-driver-login-dialog',
  imports: [ModalComponent, ButtonComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './driver-login-dialog.component.scss',
  template: `
    @if (result(); as r) {
      <app-modal title="Login created" [dismissible]="false">
        <div class="warning" role="alert">
          <app-icon name="triangle-alert" [size]="18" />
          <span>
            <strong>Copy these credentials now.</strong> The temporary password is shown only once and cannot be viewed again.
            {{ r.displayName }} must change it the first time they sign in.
          </span>
        </div>
        <dl class="credentials">
          <div class="row">
            <dt>Login ID</dt>
            <dd><code>{{ r.orgUserId }}</code></dd>
            <app-button variant="secondary" size="sm" aria-label="Copy login ID" (click)="copy(r.orgUserId, 'Login ID')"><app-icon name="copy" [size]="14" />Copy</app-button>
          </div>
          <div class="row">
            <dt>Temporary password</dt>
            <dd><code>{{ r.temporaryPassword }}</code></dd>
            <app-button variant="secondary" size="sm" aria-label="Copy password" (click)="copy(r.temporaryPassword, 'Password')"><app-icon name="copy" [size]="14" />Copy</app-button>
          </div>
          <div class="row plain"><dt>Role</dt><dd>{{ r.role }}</dd></div>
        </dl>
        <p class="email-note" [class.sent]="r.emailSent">
          @if (r.emailSent) {
            <app-icon name="mail" [size]="16" /> Credentials were also emailed to {{ r.emailSentTo }}.
          } @else {
            <app-icon name="info" [size]="16" /> No email was sent (no email address on record, or it could not be delivered), so share these credentials directly.
          }
        </p>
        <ng-container modal-footer>
          <app-button (click)="created.emit(r)">Done</app-button>
        </ng-container>
      </app-modal>
    } @else {
      <app-modal title="Create login" [dismissible]="!saving()" (closed)="closed.emit()">
        <p>Create a login for <strong>{{ driverName() }}</strong> with the <strong>Driver</strong> role.</p>
        <p class="hint">A Login ID and a random temporary password are generated and shown once. The password must be changed on first sign-in.</p>
        <ng-container modal-footer>
          <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
          <app-button [disabled]="saving()" (click)="create()">{{ saving() ? 'Creating…' : 'Create login' }}</app-button>
        </ng-container>
      </app-modal>
    }
  `,
})
export class DriverLoginDialogComponent {
  private readonly service = inject(TransportService);
  private readonly toast = inject(ToastService);

  readonly driverId = input.required<string>();
  readonly driverName = input<string>('');
  /** Emitted when the user is done with the credentials. */
  readonly created = output<DriverLoginCreated>();
  readonly closed = output<void>();

  protected readonly saving = signal(false);
  protected readonly result = signal<DriverLoginCreated | null>(null);

  protected create(): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.service
      .createDriverLogin(this.driverId())
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (r) => this.result.set(r),
        error: () => undefined, // the server's reason (already has a login, inactive driver, ...) is toasted by the interceptor
      });
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
