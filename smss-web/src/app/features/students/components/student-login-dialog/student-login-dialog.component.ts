import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ButtonComponent, IconComponent, ModalComponent } from '../../../../shared/components';
import { deliveryLabel } from '../../../parent-account/models/parent-account.model';
import { StudentLoginCreated } from '../../models/student.model';
import { StudentService } from '../../services/student.service';

/**
 * Creates the student's login. There is NO password to copy: the login is made without one and the PRIMARY GUARDIAN is sent a one-time link (WhatsApp and email) to choose it. Step 1 asks for confirmation;
 * step 2 shows the login ID and what happened to the link, with the reason when it could not be sent (no parent consent, no WhatsApp or email on file). A class teacher can do this for their own section.
 */
@Component({
  selector: 'app-student-login-dialog',
  imports: [ModalComponent, ButtonComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './student-login-dialog.component.scss',
  template: `
    @if (result(); as r) {
      <app-modal title="Student login created" [dismissible]="false">
        <dl class="credentials">
          <div class="row">
            <dt>Login ID</dt>
            <dd><code data-field="login-id">{{ r.orgUserId }}</code></dd>
          </div>
        </dl>

        <p class="email-note" [class.sent]="r.linkSent" data-note="link" role="status">
          <app-icon [name]="r.linkSent ? 'mail' : 'info'" [size]="16" /> {{ r.message }}
        </p>
        @if (r.deliveries.length > 0) {
          <ul data-list="deliveries">
            @for (d of r.deliveries; track d.channel) { <li>{{ label(d) }}</li> }
          </ul>
        }
        <p class="hint">The student can sign in with this login ID once the parent has chosen the password. Nobody, including you, can see it.</p>

        <ng-container modal-footer>
          <app-button (click)="created.emit(r)">Done</app-button>
        </ng-container>
      </app-modal>
    } @else {
      <app-modal title="Create student login" [dismissible]="!saving()" (closed)="closed.emit()">
        <p>
          Create a login for <strong>{{ studentName() }}</strong>? The student's own ID becomes the login ID.
        </p>
        <p class="hint">No password is made. The primary guardian is sent a one-time link on WhatsApp and email to choose the password.</p>

        <ng-container modal-footer>
          <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
          <app-button [disabled]="saving()" (click)="create()" data-action="create">{{ saving() ? 'Creating…' : 'Create login' }}</app-button>
        </ng-container>
      </app-modal>
    }
  `,
})
export class StudentLoginDialogComponent {
  private readonly students = inject(StudentService);

  readonly studentId = input.required<string>();
  readonly studentName = input<string>('');

  /** Emitted when the user is done. */
  readonly created = output<StudentLoginCreated>();
  readonly closed = output<void>();

  protected readonly saving = signal(false);
  protected readonly result = signal<StudentLoginCreated | null>(null);
  protected readonly label = deliveryLabel;

  protected create(): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.students
      .createLogin(this.studentId())
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (r) => this.result.set(r),
        error: () => undefined, // the server's reason (already has a login, not your section, ...) is toasted by the interceptor
      });
  }
}
