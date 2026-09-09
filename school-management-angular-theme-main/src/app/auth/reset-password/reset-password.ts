import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { IconComponent } from '../../shared/components/icon/icon';

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="auth-card animate-in">
      <h2>Set a new password</h2>
      <p class="text-secondary sub">Choose a strong password you haven't used before.</p>
      <form [formGroup]="form" (ngSubmit)="submit()">
        <label class="field-label">New Password</label>
        <input class="input" type="password" formControlName="password" placeholder="••••••••" />
        @if (form.controls.password.invalid && form.controls.password.touched) {
          <div class="field-error">Password must be at least 6 characters.</div>
        }
        <label class="field-label" style="margin-top:16px">Confirm Password</label>
        <input class="input" type="password" formControlName="confirm" placeholder="••••••••" />
        @if (form.hasError('mismatch') && form.controls.confirm.touched) {
          <div class="field-error">Passwords do not match.</div>
        }
        <button type="submit" class="btn btn-primary submit-btn" [disabled]="form.invalid">
          <app-icon name="check" [size]="16" /> Reset Password
        </button>
      </form>
      <p class="switch-auth text-secondary">
        <a routerLink="/auth/login" class="link"><app-icon name="arrow-left" [size]="13" /> Back to Sign In</a>
      </p>
    </div>
  `,
  styles: [`
    .auth-card { width: 100%; max-width: 400px; }
    h2 { font-size: 24px; margin-bottom: 6px; }
    .sub { margin-bottom: var(--space-6); font-size: 13.5px; }
    .link { color: var(--primary); font-weight: 600; display: inline-flex; align-items: center; gap: 4px; }
    .submit-btn { width: 100%; padding: 11px; font-size: 14px; margin-top: var(--space-6); }
    .switch-auth { text-align: center; margin-top: var(--space-6); font-size: 13px; }
  `],
})
export class ResetPasswordComponent {
  private fb = inject(FormBuilder);
  private router = inject(Router);

  form = this.fb.nonNullable.group({
    password: ['', [Validators.required, Validators.minLength(6)]],
    confirm: ['', Validators.required],
  }, { validators: (g) => g.get('password')?.value === g.get('confirm')?.value ? null : { mismatch: true } });

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.router.navigate(['/auth/login']);
  }
}
