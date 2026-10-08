import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { ToastService } from '../../../core/services/toast.service';
import { ButtonComponent, CardComponent, InputComponent, PageHeaderComponent } from '../../../shared/components';

const MIN_LENGTH = 8; // server rule

function matchesNewPassword(control: AbstractControl): ValidationErrors | null {
  const parent = control.parent;
  return parent && control.value !== parent.get('newPassword')?.value ? { mismatch: true } : null;
}

@Component({
  selector: 'app-change-password',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, InputComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './change-password.component.scss',
  templateUrl: './change-password.component.html',
})
export class ChangePasswordComponent {
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  protected readonly firstLogin = this.auth.user()?.isFirstLogin ?? false;
  protected readonly form = new FormGroup({
    currentPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    newPassword: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(MIN_LENGTH), Validators.maxLength(100)] }),
    confirmPassword: new FormControl('', { nonNullable: true, validators: [Validators.required, matchesNewPassword] }),
  });
  protected readonly submitted = signal(false);
  protected readonly saving = signal(false);

  constructor() {
    this.form.controls.newPassword.valueChanges
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe(() => this.form.controls.confirmPassword.updateValueAndValidity());
  }

  protected error(name: 'currentPassword' | 'newPassword' | 'confirmPassword'): string | undefined {
    const c = this.form.controls[name];
    if (c.valid || !(c.touched || this.submitted())) return undefined;
    if (c.hasError('required')) return 'This field is required';
    if (c.hasError('minlength')) return `Use at least ${MIN_LENGTH} characters`;
    if (c.hasError('maxlength')) return 'Maximum 100 characters';
    if (c.hasError('mismatch')) return 'Passwords do not match';
    return 'Invalid value';
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    const { currentPassword, newPassword } = this.form.getRawValue();
    this.saving.set(true);
    this.auth
      .changePassword({ currentPassword, newPassword })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.toast.success('Password changed. Other devices have been signed out.');
          void this.router.navigateByUrl('/dashboard');
        },
        error: () => undefined, // the server's reason (e.g. wrong current password) is toasted by the interceptor
      });
  }
}
