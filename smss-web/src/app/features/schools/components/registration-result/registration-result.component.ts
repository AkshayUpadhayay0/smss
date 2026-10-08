import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent } from '../../../../shared/components';
import { SchoolRegistrationResponse } from '../../models/school.model';

/** Shown instead of the form after a successful registration. The password cannot be fetched again. */
@Component({
  selector: 'app-registration-result',
  imports: [ButtonComponent, CardComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './registration-result.component.scss',
  template: `
    <app-card>
      <div class="result">
        <span class="badge"><app-icon name="circle-check" [size]="28" /></span>
        <h2>School registered successfully</h2>
        <p class="text-muted">
          {{ result().school.schoolName }} ({{ result().school.schoolCode }}) can now sign in with the credentials below.
        </p>

        <div class="warning" role="alert">
          <app-icon name="triangle-alert" [size]="18" />
          <span>
            <strong>Copy these credentials now.</strong> The temporary password is shown only once and cannot be viewed again after you leave this page.
          </span>
        </div>

        <dl class="credentials">
          <div class="row">
            <dt>Username</dt>
            <dd><code>{{ result().username }}</code></dd>
            <app-button variant="secondary" size="sm" aria-label="Copy username" (click)="copy(result().username, 'Username')">
              <app-icon name="copy" [size]="14" />Copy
            </app-button>
          </div>
          <div class="row">
            <dt>Temporary password</dt>
            <dd><code>{{ result().temporaryPassword }}</code></dd>
            <app-button variant="secondary" size="sm" aria-label="Copy password" (click)="copy(result().temporaryPassword, 'Password')">
              <app-icon name="copy" [size]="14" />Copy
            </app-button>
          </div>
        </dl>

        <p class="email-note" [class.sent]="result().emailSent">
          @if (result().emailSent) {
            <app-icon name="mail" [size]="16" /> Credentials were also emailed to {{ result().emailSentTo }}.
          } @else {
            <app-icon name="info" [size]="16" /> No email was sent, so share these credentials with the school directly.
          }
        </p>

        <app-button (click)="done.emit()">Done</app-button>
      </div>
    </app-card>
  `,
})
export class RegistrationResultComponent {
  private readonly toast = inject(ToastService);

  readonly result = input.required<SchoolRegistrationResponse>();
  readonly done = output<void>();

  protected async copy(value: string, label: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(value);
      this.toast.success(`${label} copied to clipboard.`);
    } catch {
      this.toast.warning('Could not copy automatically — please select and copy it manually.');
    }
  }
}
