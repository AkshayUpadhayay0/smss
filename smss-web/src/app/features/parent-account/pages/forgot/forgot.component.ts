import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ButtonComponent, IconComponent, InputComponent } from '../../../../shared/components';
import { ParentAccountService } from '../../services/parent-account.service';

/** Forgot ID or password: the registered WhatsApp number or email in; the SAME answer out, whatever was typed. */
@Component({
  selector: 'app-parent-forgot',
  imports: [ReactiveFormsModule, RouterLink, ButtonComponent, IconComponent, InputComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../parent-pages.scss',
  template: `
    <div class="heading">
      <h2>Forgot ID or password</h2>
      <p class="text-muted">Enter the WhatsApp number or email the school has for you.</p>
    </div>

    @if (sent()) {
      <div class="ok" role="status" data-state="sent">
        <p><strong>Check your WhatsApp or email.</strong></p>
        <p>If that matches a parent account, we are sending your login ID and a link to choose a new password. The link works once and expires in an hour. If nothing arrives within a few minutes, please contact your school.</p>
        <a class="button-link" routerLink="/login" data-link="login">Back to sign in</a>
      </div>
    } @else {
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        @if (error(); as message) { <p class="alert" role="alert" data-note="error"><app-icon name="circle-alert" [size]="16" />{{ message }}</p> }
        <app-input label="WhatsApp number or email" prefixIcon="user" placeholder="+91 98765 43210 or name@example.com" formControlName="contact" [errorText]="fieldError()" />
        <app-button type="submit" [block]="true" size="lg" [disabled]="saving()">{{ saving() ? 'Sending…' : 'Send me the link' }}</app-button>
        <a class="link" routerLink="/login" data-link="back">Back to sign in</a>
      </form>
    }
  `,
})
export class ParentForgotComponent {
  private readonly api = inject(ParentAccountService);

  protected readonly form = new FormGroup({ contact: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(150)] }) });
  protected readonly saving = signal(false);
  protected readonly sent = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly submitted = signal(false);

  protected fieldError(): string | undefined {
    const c = this.form.controls.contact;
    return c.invalid && (c.touched || this.submitted()) ? 'Enter your WhatsApp number or email' : undefined;
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.error.set(null);
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.saving.set(true);
    this.api.forgot(this.form.controls.contact.value.trim()).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => this.sent.set(true),
      // a rate-limit or network problem is the only thing that is ever different, and it says nothing about the contact
      error: (e: { status?: number }) => this.error.set(e?.status === 429 ? 'Too many requests. Please wait a few minutes and try again.' : e?.status === 0 ? 'Cannot reach the server. Check your connection and try again.' : 'Something went wrong. Please try again.'),
    });
  }
}
