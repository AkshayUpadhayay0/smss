import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { IconComponent } from '../../shared/components/icon/icon';
import { AuthService } from '../../core/services/auth.service';
import { UserRole } from '../../core/models/school.models';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="auth-card animate-in">
      <h2>Welcome back</h2>
      <p class="text-secondary sub">Sign in to access your school management dashboard.</p>

      <div class="role-tabs">
        @for (r of roles; track r) {
          <button type="button" class="role-tab" [class.active]="role() === r" (click)="role.set(r)">{{ r }}</button>
        }
      </div>

      <form [formGroup]="form" (ngSubmit)="submit()">
        <label class="field-label">Email Address</label>
        <div class="input-icon">
          <app-icon name="mail" [size]="16" />
          <input class="input" type="email" formControlName="email" placeholder="you@greenvalley.edu.in" />
        </div>
        @if (form.controls.email.invalid && form.controls.email.touched) {
          <div class="field-error">Enter a valid email address.</div>
        }

        <label class="field-label" style="margin-top:16px">Password</label>
        <div class="input-icon">
          <app-icon name="lock" [size]="16" />
          <input class="input" [type]="showPassword() ? 'text' : 'password'" formControlName="password" placeholder="••••••••" />
          <button type="button" class="toggle-visibility" (click)="showPassword.set(!showPassword())">
            <app-icon [name]="showPassword() ? 'eye' : 'eye'" [size]="16" />
          </button>
        </div>
        @if (form.controls.password.invalid && form.controls.password.touched) {
          <div class="field-error">Password must be at least 6 characters.</div>
        }

        <div class="row-between">
          <label class="checkbox-label">
            <input type="checkbox" formControlName="remember" /> Remember me
          </label>
          <a routerLink="/auth/forgot-password" class="link">Forgot password?</a>
        </div>

        <button type="submit" class="btn btn-primary submit-btn" [disabled]="form.invalid">
          Sign In <app-icon name="arrow-right" [size]="16" />
        </button>
      </form>

      <p class="switch-auth text-secondary">
        Don't have an account? <a routerLink="/auth/register" class="link">Create one</a>
      </p>
    </div>
  `,
  styles: [`
    .auth-card { width: 100%; max-width: 400px; }
    h2 { font-size: 24px; margin-bottom: 6px; }
    .sub { margin-bottom: var(--space-6); font-size: 13.5px; }
    .role-tabs { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: var(--space-6); }
    .role-tab {
      padding: 6px 12px; border-radius: var(--radius-full); border: 1px solid var(--border-strong);
      background: var(--surface); color: var(--text-secondary); font-size: 11.5px; font-weight: 600; cursor: pointer;
    }
    .role-tab.active { background: var(--primary); border-color: var(--primary); color: #fff; }
    .input-icon { position: relative; display: flex; align-items: center; }
    .input-icon app-icon:first-child { position: absolute; left: 12px; color: var(--text-muted); }
    .input-icon .input { padding-left: 36px; padding-right: 36px; }
    .toggle-visibility { position: absolute; right: 10px; background: none; border: none; color: var(--text-muted); cursor: pointer; padding: 4px; }
    .row-between { display: flex; align-items: center; justify-content: space-between; margin: var(--space-4) 0 var(--space-5); font-size: 12.5px; }
    .checkbox-label { display: flex; align-items: center; gap: 6px; color: var(--text-secondary); }
    .link { color: var(--primary); font-weight: 600; }
    .submit-btn { width: 100%; padding: 11px; font-size: 14px; }
    .switch-auth { text-align: center; margin-top: var(--space-6); font-size: 13px; }
  `],
})
export class LoginComponent {
  private fb = inject(FormBuilder);
  private auth = inject(AuthService);
  private router = inject(Router);

  roles: UserRole[] = ['Admin', 'Teacher', 'Student', 'Parent', 'Accountant'];
  role = signal<UserRole>('Admin');
  showPassword = signal(false);

  form = this.fb.nonNullable.group({
    email: ['priya.sharma@greenvalley.edu.in', [Validators.required, Validators.email]],
    password: ['demo123', [Validators.required, Validators.minLength(6)]],
    remember: [true],
  });

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { email, password } = this.form.getRawValue();
    this.auth.login(email, password, this.role());
    this.router.navigate(['/dashboard']);
  }
}
