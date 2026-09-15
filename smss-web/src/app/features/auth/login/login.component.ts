import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ButtonComponent } from '../../../shared/components/button/button.component';
import { InputComponent } from '../../../shared/components/input/input.component';
import { CheckboxComponent } from '../../../shared/components/checkbox/checkbox.component';
import { AlertComponent } from '../../../shared/components/alert/alert.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { AuthService, ToastService } from '../../../core/services';

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, ButtonComponent, InputComponent, CheckboxComponent, AlertComponent, IconComponent],
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
    email: ['admin@example.com', [Validators.required, Validators.email]],
    password: ['demo1234', [Validators.required]],
    rememberMe: [true],
  });

  errorFor(controlName: 'email' | 'password'): string | undefined {
    const control = this.form.get(controlName);
    if (!control || !control.touched || control.valid) return undefined;
    if (control.hasError('required')) return 'This field is required.';
    if (control.hasError('email')) return 'Please enter a valid email address.';
    return undefined;
  }

  onSubmit(): void {
    this.errorMessage.set(null);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.loading.set(true);
    const { email, password, rememberMe } = this.form.getRawValue();

    // Simulate a brief network delay so the loading state is visible, like a real API call.
    setTimeout(() => {
      const result = this.authService.login(email!, password!, !!rememberMe);
      this.loading.set(false);

      if (!result.success) {
        this.errorMessage.set(result.error ?? 'Unable to sign in.');
        return;
      }

      this.toastService.success('Welcome back!', 'You have signed in successfully.');
      this.router.navigate(['/dashboard']);
    }, 500);
  }
}
