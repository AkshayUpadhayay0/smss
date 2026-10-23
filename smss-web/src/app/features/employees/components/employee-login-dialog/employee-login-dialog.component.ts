import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent, ModalComponent } from '../../../../shared/components';
import { AssignableRole, EmployeeLoginCreated } from '../../models/employee.model';
import { EmployeeService } from '../../services/employee.service';

/**
 * Creates the employee's login. Step 1: choose one or more roles (the list comes from the server: active roles a school may hand out).
 * Step 2: the generated Login ID and temporary password are shown ONCE (the server cannot return the password again).
 */
@Component({
  selector: 'app-employee-login-dialog',
  imports: [ModalComponent, ButtonComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './employee-login-dialog.component.scss',
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
          <div class="row plain"><dt>Roles</dt><dd>{{ r.roles.join(', ') }}</dd></div>
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
        <p>
          Create a login for <strong>{{ employeeName() }}</strong>. What they can do in the system depends on the roles you choose here
          (their designation is only a job title).
        </p>

        @if (roles().length === 0) {
          <p class="hint">No roles are available to assign.</p>
        } @else {
          <fieldset class="roles">
            <legend>Roles <span class="required">*</span></legend>
            @for (role of roles(); track role.roleId) {
              <label class="role">
                <input type="checkbox" [checked]="selected().has(role.roleId)" (change)="toggle(role.roleId)" />
                <span class="name">{{ role.roleName }}</span>
                @if (role.description) {
                  <span class="desc">{{ role.description }}</span>
                }
              </label>
            }
          </fieldset>
        }
        @if (showError()) {
          <p class="error" role="alert">Choose at least one role.</p>
        }
        <p class="hint">A Login ID and a random temporary password are generated and shown once. The password must be changed on first sign-in.</p>

        <ng-container modal-footer>
          <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
          <app-button [disabled]="saving() || roles().length === 0" (click)="create()">{{ saving() ? 'Creating…' : 'Create login' }}</app-button>
        </ng-container>
      </app-modal>
    }
  `,
})
export class EmployeeLoginDialogComponent {
  private readonly employees = inject(EmployeeService);
  private readonly toast = inject(ToastService);

  readonly employeeId = input.required<string>();
  readonly employeeName = input<string>('');
  /** The roles a school may assign, as the server returned them (nothing is hardcoded here). */
  readonly roles = input<AssignableRole[]>([]);

  /** Emitted when the user is done with the credentials. */
  readonly created = output<EmployeeLoginCreated>();
  readonly closed = output<void>();

  protected readonly selected = signal<ReadonlySet<number>>(new Set());
  protected readonly saving = signal(false);
  protected readonly result = signal<EmployeeLoginCreated | null>(null);
  protected readonly attempted = signal(false);
  protected readonly showError = computed(() => this.attempted() && this.selected().size === 0);

  protected toggle(roleId: number): void {
    const next = new Set(this.selected());
    if (!next.delete(roleId)) next.add(roleId);
    this.selected.set(next);
  }

  protected create(): void {
    if (this.saving()) return;
    this.attempted.set(true);
    if (this.selected().size === 0) return;
    this.saving.set(true);
    this.employees
      .createLogin(this.employeeId(), [...this.selected()])
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (r) => this.result.set(r),
        error: () => undefined, // the server's reason (already has a login, inactive record, ...) is toasted by the interceptor
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
