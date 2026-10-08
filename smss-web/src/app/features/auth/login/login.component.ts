import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { ButtonComponent, IconComponent, InputComponent } from '../../../shared/components';

/** Only same-app absolute paths are allowed as a post-login target (prevents open redirects). */
export function safeReturnUrl(url: string | undefined): string | null {
  return url && url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/login') ? url : null;
}

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, ButtonComponent, IconComponent, InputComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './login.component.scss',
  templateUrl: './login.component.html',
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Bound from the ?returnUrl= query param (withComponentInputBinding). */
  readonly returnUrl = input<string>();

  protected readonly form = new FormGroup({
    username: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    rememberMe: new FormControl(false, { nonNullable: true }),
  });
  protected readonly submitted = signal(false);
  protected readonly loading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected fieldError(name: 'username' | 'password'): string | undefined {
    const c = this.form.controls[name];
    if (!c.invalid || !(c.touched || this.submitted())) return undefined;
    return name === 'username' ? 'Enter your username' : 'Enter your password';
  }

  protected submit(): void {
    if (this.loading()) return;
    this.submitted.set(true);
    this.errorMessage.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { username, password, rememberMe } = this.form.getRawValue();
    this.loading.set(true);
    this.auth
      .login({ username: username.trim(), password, rememberMe })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (user) => {
          // A temporary password (first login) must be replaced before anything else.
          const target = user.isFirstLogin ? '/change-password' : (safeReturnUrl(this.returnUrl()) ?? '/dashboard');
          void this.router.navigateByUrl(target);
        },
        error: (e: { error?: { message?: string }; status?: number }) => {
          this.form.controls.password.reset('');
          this.errorMessage.set(
            e?.status === 0 ? 'Cannot reach the server. Check your connection and try again.' : (e?.error?.message ?? 'Sign in failed. Please try again.'),
          );
        },
      });
  }
}
