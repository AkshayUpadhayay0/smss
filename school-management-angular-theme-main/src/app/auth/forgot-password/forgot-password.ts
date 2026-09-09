import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { IconComponent } from '../../shared/components/icon/icon';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="auth-card animate-in">
      @if (!sent()) {
        <h2>Forgot your password?</h2>
        <p class="text-secondary sub">Enter your registered email and we'll send you a reset link.</p>
        <form [formGroup]="form" (ngSubmit)="submit()">
          <label class="field-label">Email Address</label>
          <input class="input" type="email" formControlName="email" placeholder="you@greenvalley.edu.in" />
          @if (form.controls.email.invalid && form.controls.email.touched) {
            <div class="field-error">Enter a valid email address.</div>
          }
          <button type="submit" class="btn btn-primary submit-btn" [disabled]="form.invalid">Send Reset Link</button>
        </form>
      } @else {
        <div class="success-box">
          <div class="icon-circle"><app-icon name="mail" [size]="26" /></div>
          <h2>Check your inbox</h2>
          <p class="text-secondary sub">We've sent a password reset link to {{ form.value.email }}.</p>
          <button class="btn btn-primary submit-btn" (click)="router.navigate(['/auth/reset-password'])">Continue to Reset</button>
        </div>
      }
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
    .submit-btn { width: 100%; padding: 11px; font-size: 14px; margin-top: var(--space-2); }
    .switch-auth { text-align: center; margin-top: var(--space-6); font-size: 13px; }
    .success-box { text-align: center; }
    .icon-circle {
      width: 60px; height: 60px; border-radius: var(--radius-full); background: var(--primary-light); color: var(--primary);
      display: flex; align-items: center; justify-content: center; margin: 0 auto var(--space-4);
    }
  `],
})
export class ForgotPasswordComponent {
  private fb = inject(FormBuilder);
  router = inject(Router);
  sent = signal(false);

  form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.sent.set(true);
  }
}
