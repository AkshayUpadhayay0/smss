import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ButtonComponent } from '../../../shared/components/button/button.component';
import { InputComponent } from '../../../shared/components/input/input.component';
import { CheckboxComponent } from '../../../shared/components/checkbox/checkbox.component';
import { AlertComponent } from '../../../shared/components/alert/alert.component';
import { ThemeSelectorComponent } from '../../../shared/components/theme-selector/theme-selector.component';
import { AuthService, ToastService } from '../../../core/services';
import { ThemeName } from '../../../core/models';

function passwordsMatchValidator(control: AbstractControl): ValidationErrors | null {
  const password = control.get('password')?.value;
  const confirmPassword = control.get('confirmPassword')?.value;
  if (password && confirmPassword && password !== confirmPassword) {
    return { passwordsMismatch: true };
  }
  return null;
}

@Component({
  selector: 'app-register-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    ButtonComponent,
    InputComponent,
    CheckboxComponent,
    AlertComponent,
    ThemeSelectorComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './register.component.html',
  styleUrl: './register.component.scss',
})
export class RegisterPageComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toastService = inject(ToastService);

  readonly loading = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.fb.group(
    {
      organizationName: ['', [Validators.required, Validators.minLength(2)]],
      organizationCode: ['', [Validators.required, Validators.minLength(2)]],
      adminFullName: ['', [Validators.required, Validators.minLength(2)]],
      adminEmail: ['', [Validators.required, Validators.email]],
      phone: ['', [Validators.required]],
      password: ['', [Validators.required, Validators.minLength(8)]],
      confirmPassword: ['', [Validators.required]],
      theme: ['default' as ThemeName, [Validators.required]],
      acceptTerms: [false, [Validators.requiredTrue]],
    },
    { validators: passwordsMatchValidator },
  );

  errorFor(controlName: string): string | undefined {
    const control = this.form.get(controlName);
    if (!control || !control.touched || control.valid) return undefined;
    if (control.hasError('required')) return 'This field is required.';
    if (control.hasError('email')) return 'Please enter a valid email address.';
    if (control.hasError('minlength')) {
      return `Must be at least ${control.getError('minlength').requiredLength} characters.`;
    }
    return undefined;
  }

  get confirmPasswordError(): string | undefined {
    const confirmControl = this.form.get('confirmPassword');
    if (!confirmControl?.touched) return undefined;
    if (confirmControl.hasError('required')) return 'Please confirm your password.';
    if (this.form.hasError('passwordsMismatch')) return 'Passwords do not match.';
    return undefined;
  }

  selectTheme(theme: ThemeName): void {
    this.form.patchValue({ theme });
  }

  onSubmit(): void {
    this.errorMessage.set(null);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.loading.set(true);
    const payload = this.form.getRawValue();

    setTimeout(() => {
      const result = this.authService.register({
        organizationName: payload.organizationName!,
        organizationCode: payload.organizationCode!,
        adminFullName: payload.adminFullName!,
        adminEmail: payload.adminEmail!,
        password: payload.password!,
        confirmPassword: payload.confirmPassword!,
        phone: payload.phone!,
        theme: payload.theme!,
        acceptTerms: payload.acceptTerms!,
      });
      this.loading.set(false);

      if (!result.success) {
        this.errorMessage.set(result.error ?? 'Unable to complete registration.');
        return;
      }

      this.toastService.success('Organization created', 'Your account is ready — welcome aboard!');
      this.router.navigate(['/dashboard']);
    }, 600);
  }
}
