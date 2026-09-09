import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { IconComponent } from '../../shared/components/icon/icon';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="auth-card animate-in">
      <h2>Create your account</h2>
      <p class="text-secondary sub">Set up admin access to your school's management portal.</p>

      <form [formGroup]="form" (ngSubmit)="submit()">
        <div class="grid-2 gap-3">
          <div>
            <label class="field-label">First Name</label>
            <input class="input" formControlName="firstName" placeholder="Priya" />
          </div>
          <div>
            <label class="field-label">Last Name</label>
            <input class="input" formControlName="lastName" placeholder="Sharma" />
          </div>
        </div>

        <label class="field-label" style="margin-top:16px">School Name</label>
        <input class="input" formControlName="schoolName" placeholder="Green Valley Public School" />

        <label class="field-label" style="margin-top:16px">Work Email</label>
        <input class="input" type="email" formControlName="email" placeholder="you@school.edu.in" />
        @if (form.controls.email.invalid && form.controls.email.touched) {
          <div class="field-error">Enter a valid email address.</div>
        }

        <label class="field-label" style="margin-top:16px">Password</label>
        <input class="input" type="password" formControlName="password" placeholder="Create a strong password" />
        @if (form.controls.password.invalid && form.controls.password.touched) {
          <div class="field-error">Password must be at least 6 characters.</div>
        }

        <label class="checkbox-label" style="margin-top:16px">
          <input type="checkbox" formControlName="terms" />
          I agree to the Terms of Service and Privacy Policy
        </label>

        <button type="submit" class="btn btn-primary submit-btn" [disabled]="form.invalid">
          Create Account <app-icon name="arrow-right" [size]="16" />
        </button>
      </form>

      <p class="switch-auth text-secondary">
        Already have an account? <a routerLink="/auth/login" class="link">Sign in</a>
      </p>
    </div>
  `,
  styles: [`
    .auth-card { width: 100%; max-width: 440px; }
    h2 { font-size: 24px; margin-bottom: 6px; }
    .sub { margin-bottom: var(--space-6); font-size: 13.5px; }
    .checkbox-label { display: flex; align-items: flex-start; gap: 8px; color: var(--text-secondary); font-size: 12.5px; }
    .link { color: var(--primary); font-weight: 600; }
    .submit-btn { width: 100%; padding: 11px; font-size: 14px; margin-top: var(--space-6); }
    .switch-auth { text-align: center; margin-top: var(--space-6); font-size: 13px; }
  `],
})
export class RegisterComponent {
  private fb = inject(FormBuilder);
  private auth = inject(AuthService);
  private router = inject(Router);

  form = this.fb.nonNullable.group({
    firstName: ['', Validators.required],
    lastName: ['', Validators.required],
    schoolName: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(6)]],
    terms: [false, Validators.requiredTrue],
  });

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { email } = this.form.getRawValue();
    this.auth.login(email, 'demo', 'Admin');
    this.router.navigate(['/dashboard']);
  }
}
