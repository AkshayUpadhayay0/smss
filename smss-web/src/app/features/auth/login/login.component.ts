import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { OnboardingService } from '../../../core/services/onboarding.service';
import { ButtonComponent, IconComponent, InputComponent } from '../../../shared/components';

/** Only same-app absolute paths are allowed as a post-login target (prevents open redirects). */
export function safeReturnUrl(url: string | undefined): string | null {
  return url && url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/login') ? url : null;
}

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink, ButtonComponent, IconComponent, InputComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './login.component.scss',
  templateUrl: './login.component.html',
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly onboarding = inject(OnboardingService);

  /** Bound from the ?returnUrl= query param (withComponentInputBinding). */
  readonly returnUrl = input<string>();

  protected readonly form = new FormGroup({
    loginId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    rememberMe: new FormControl(false, { nonNullable: true }),
  });
  protected readonly submitted = signal(false);
  protected readonly loading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected fieldError(name: 'loginId' | 'password'): string | undefined {
    const c = this.form.controls[name];
    if (!c.invalid || !(c.touched || this.submitted())) return undefined;
    return name === 'loginId' ? 'Enter your login ID' : 'Enter your password';
  }

  protected submit(): void {
    if (this.loading()) return;
    this.submitted.set(true);
    this.errorMessage.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { loginId, password, rememberMe } = this.form.getRawValue();
    this.loading.set(true);
    this.auth
      .login({ loginId: loginId.trim(), password, rememberMe })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (user) => {
          // A temporary password (first login) must be replaced before anything else.
          if (user.isFirstLogin) {
            void this.router.navigateByUrl('/change-password');
            return;
          }
          // A parent always lands on the family screen, even with one child
          if (user.roles.includes('Guardian') && !user.roles.includes('Student')) { void this.router.navigateByUrl('/family'); return; }
          // A School Admin whose school setup is unfinished is routed to the next setup step instead
          this.onboarding.landingFor(safeReturnUrl(this.returnUrl()) ?? '/dashboard').subscribe((target) => void this.router.navigateByUrl(target));
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
