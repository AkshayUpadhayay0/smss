import { ChangeDetectionStrategy, Component, ElementRef, inject, signal, ViewChildren, QueryList, AfterViewInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { IconComponent } from '../../shared/components/icon/icon';

@Component({
  selector: 'app-otp-verification',
  standalone: true,
  imports: [FormsModule, RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="auth-card animate-in">
      <div class="icon-circle"><app-icon name="smartphone" [size]="26" /></div>
      <h2>Verify your identity</h2>
      <p class="text-secondary sub">Enter the 6-digit code sent to +91 98••••••10.</p>

      <div class="otp-row">
        @for (i of [0,1,2,3,4,5]; track i) {
          <input
            #otpInput
            class="otp-box"
            maxlength="1"
            inputmode="numeric"
            [(ngModel)]="digits[i]"
            (input)="onInput(i, $event)"
            (keydown.backspace)="onBackspace(i, $event)"
          />
        }
      </div>

      <button type="button" class="btn btn-primary submit-btn" [disabled]="!isComplete()" (click)="submit()">
        Verify & Continue
      </button>

      <p class="switch-auth text-secondary">
        Didn't receive the code? <a class="link" (click)="resend()">Resend OTP</a>
      </p>
      <p class="switch-auth text-secondary">
        <a routerLink="/auth/login" class="link"><app-icon name="arrow-left" [size]="13" /> Back to Sign In</a>
      </p>
    </div>
  `,
  styles: [`
    .auth-card { width: 100%; max-width: 400px; text-align: center; }
    .icon-circle {
      width: 60px; height: 60px; border-radius: var(--radius-full); background: var(--primary-light); color: var(--primary);
      display: flex; align-items: center; justify-content: center; margin: 0 auto var(--space-4);
    }
    h2 { font-size: 22px; margin-bottom: 6px; }
    .sub { margin-bottom: var(--space-6); font-size: 13.5px; }
    .otp-row { display: flex; gap: 10px; justify-content: center; margin-bottom: var(--space-6); }
    .otp-box {
      width: 44px; height: 52px; text-align: center; font-size: 20px; font-weight: 700;
      border-radius: var(--radius-md); border: 1px solid var(--border-strong); background: var(--surface); color: var(--text-primary);
    }
    .otp-box:focus { outline: none; border-color: var(--primary); box-shadow: 0 0 0 3px rgba(var(--primary-rgb), 0.15); }
    .submit-btn { width: 100%; padding: 11px; font-size: 14px; }
    .link { color: var(--primary); font-weight: 600; cursor: pointer; }
    .switch-auth { margin-top: var(--space-4); font-size: 13px; }
  `],
})
export class OtpVerificationComponent implements AfterViewInit {
  @ViewChildren('otpInput') inputs!: QueryList<ElementRef<HTMLInputElement>>;
  private router = inject(Router);
  digits: string[] = ['', '', '', '', '', ''];

  ngAfterViewInit(): void {
    this.inputs.first?.nativeElement.focus();
  }

  onInput(index: number, event: Event): void {
    const value = (event.target as HTMLInputElement).value.replace(/\D/g, '');
    this.digits[index] = value.slice(-1);
    if (value && index < 5) {
      this.inputs.get(index + 1)?.nativeElement.focus();
    }
  }

  onBackspace(index: number, event: Event): void {
    const ke = event as KeyboardEvent;
    if (!this.digits[index] && index > 0) {
      this.inputs.get(index - 1)?.nativeElement.focus();
    }
  }

  isComplete(): boolean {
    return this.digits.every(d => d.length === 1);
  }

  resend(): void {
    this.digits = ['', '', '', '', '', ''];
    this.inputs.first?.nativeElement.focus();
  }

  submit(): void {
    if (this.isComplete()) this.router.navigate(['/dashboard']);
  }
}
