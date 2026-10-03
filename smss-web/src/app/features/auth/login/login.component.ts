import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonComponent } from '../../../shared/components/button/button.component';
import { InputComponent } from '../../../shared/components/input/input.component';
import { CheckboxComponent } from '../../../shared/components/checkbox/checkbox.component';
import { AlertComponent } from '../../../shared/components/alert/alert.component';
import { AuthService, ToastService } from '../../../core/services';

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonComponent, InputComponent, CheckboxComponent, AlertComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginPageComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toastService = inject(ToastService);

  readonly loading = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.fb.group({
    username: ['', [Validators.required]],
    password: ['', [Validators.required]],
    rememberMe: [false],
  });

  errorFor(controlName: 'username' | 'password'): string | undefined {
    const control = this.form.get(controlName);
    if (!control || !control.touched || control.valid) return undefined;
    if (control.hasError('required')) return 'This field is required.';
    return undefined;
  }

  onSubmit(): void {
    if (this.loading()) return;
    this.errorMessage.set(null);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.loading.set(true);
    const { username, password, rememberMe } = this.form.getRawValue();

    this.authService.login({ username: username!.trim(), password: password!, rememberMe: !!rememberMe }).subscribe({
      next: (session) => {
        this.loading.set(false);
        if (session.isFirstLogin) {
          this.toastService.success('Welcome!', 'Please set a new password to continue.');
          this.router.navigate(['/change-password']);
          return;
        }
        this.toastService.success('Welcome back!', 'You have signed in successfully.');
        this.router.navigate(['/dashboard']);
      },
      error: (err) => {
        this.loading.set(false);
        this.errorMessage.set(this.authService.errorMessage(err, 'Unable to sign in. Please try again.'));
      },
    });
  }
}
