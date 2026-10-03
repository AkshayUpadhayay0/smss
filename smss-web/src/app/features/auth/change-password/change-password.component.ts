import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonComponent } from '../../../shared/components/button/button.component';
import { InputComponent } from '../../../shared/components/input/input.component';
import { AlertComponent } from '../../../shared/components/alert/alert.component';
import { AuthService, ToastService } from '../../../core/services';

function passwordsMatch(group: AbstractControl): ValidationErrors | null {
  const next = group.get('newPassword')?.value;
  const confirm = group.get('confirmPassword')?.value;
  return next && confirm && next !== confirm ? { mismatch: true } : null;
}

@Component({
  selector: 'app-change-password-page',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonComponent, InputComponent, AlertComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './change-password.component.html',
  styleUrl: './change-password.component.scss',
})
export class ChangePasswordPageComponent {
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly toastService = inject(ToastService);
  readonly authService = inject(AuthService);

  readonly loading = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.fb.group(
    {
      currentPassword: ['', [Validators.required]],
      newPassword: ['', [Validators.required, Validators.minLength(8)]],
      confirmPassword: ['', [Validators.required]],
    },
    { validators: passwordsMatch },
  );

  errorFor(controlName: 'currentPassword' | 'newPassword' | 'confirmPassword'): string | undefined {
    const control = this.form.get(controlName);
    if (!control || !control.touched) return undefined;
    if (control.hasError('required')) return 'This field is required.';
    if (control.hasError('minlength')) return 'Password must be at least 8 characters.';
    if (controlName === 'confirmPassword' && this.form.hasError('mismatch')) return 'Passwords do not match.';
    return undefined;
  }

  onSubmit(): void {
    if (this.loading()) return;
    this.errorMessage.set(null);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.loading.set(true);
    const { currentPassword, newPassword } = this.form.getRawValue();

    this.authService.changePassword({ currentPassword: currentPassword!, newPassword: newPassword! }).subscribe({
      next: () => {
        this.loading.set(false);
        this.toastService.success('Password updated', 'Your password has been changed.');
        this.router.navigate(['/dashboard']);
      },
      error: (err) => {
        this.loading.set(false);
        this.errorMessage.set(this.authService.errorMessage(err, 'Unable to change password. Please try again.'));
      },
    });
  }
}
