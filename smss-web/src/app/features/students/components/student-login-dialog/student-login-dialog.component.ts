import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent, ModalComponent } from '../../../../shared/components';
import { StudentLoginCreated } from '../../models/student.model';
import { StudentService } from '../../services/student.service';

/**
 * Creates the student's login. Step 1 asks for confirmation; step 2 shows the generated Login ID and temporary
 * password ONCE (the server cannot return the password again), same behaviour as the school registration result.
 */
@Component({
  selector: 'app-student-login-dialog',
  imports: [ModalComponent, ButtonComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './student-login-dialog.component.scss',
  template: `
    @if (result(); as r) {
      <app-modal title="Student login created" [dismissible]="false">
        <div class="warning" role="alert">
          <app-icon name="triangle-alert" [size]="18" />
          <span>
            <strong>Copy these credentials now.</strong> The temporary password is shown only once and cannot be viewed again.
            The student must change it the first time they sign in.
          </span>
        </div>

        <dl class="credentials">
          <div class="row">
            <dt>Login ID</dt>
            <dd><code>{{ r.orgUserId }}</code></dd>
            <app-button variant="secondary" size="sm" aria-label="Copy login ID" (click)="copy(r.orgUserId, 'Login ID')">
              <app-icon name="copy" [size]="14" />Copy
            </app-button>
          </div>
          <div class="row">
            <dt>Temporary password</dt>
            <dd><code>{{ r.temporaryPassword }}</code></dd>
            <app-button variant="secondary" size="sm" aria-label="Copy password" (click)="copy(r.temporaryPassword, 'Password')">
              <app-icon name="copy" [size]="14" />Copy
            </app-button>
          </div>
        </dl>

        <p class="email-note" [class.sent]="r.emailSent">
          @if (r.emailSent) {
            <app-icon name="mail" [size]="16" /> Credentials were also emailed to {{ r.emailSentTo }}.
          } @else {
            <app-icon name="info" [size]="16" /> No email was sent (no student or primary guardian email on record), so share these credentials directly.
          }
        </p>

        <ng-container modal-footer>
          <app-button (click)="created.emit(r)">Done</app-button>
        </ng-container>
      </app-modal>
    } @else {
      <app-modal title="Create student login" [dismissible]="!saving()" (closed)="closed.emit()">
        <p>
          Create a login for <strong>{{ studentName() }}</strong>? A Login ID and a temporary password will be generated and shown once.
        </p>
        <p class="hint">The temporary password is built from the student's name and date of birth, and must be changed on first sign-in.</p>

        <ng-container modal-footer>
          <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
          <app-button [disabled]="saving()" (click)="create()">{{ saving() ? 'Creating…' : 'Create login' }}</app-button>
        </ng-container>
      </app-modal>
    }
  `,
})
export class StudentLoginDialogComponent {
  private readonly students = inject(StudentService);
  private readonly toast = inject(ToastService);

  readonly studentId = input.required<string>();
  readonly studentName = input<string>('');

  /** Emitted when the user is done with the credentials. */
  readonly created = output<StudentLoginCreated>();
  readonly closed = output<void>();

  protected readonly saving = signal(false);
  protected readonly result = signal<StudentLoginCreated | null>(null);

  protected create(): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.students
      .createLogin(this.studentId())
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (r) => this.result.set(r),
        error: () => undefined, // the server's reason (already has a login, no short code, ...) is toasted by the interceptor
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
