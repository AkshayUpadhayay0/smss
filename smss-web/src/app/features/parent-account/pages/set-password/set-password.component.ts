import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Meta } from '@angular/platform-browser';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ButtonComponent, IconComponent, InputComponent } from '../../../../shared/components';
import { ParentAccountService } from '../../services/parent-account.service';

/**
 * The page a parent (or a student's parent) opens from the WhatsApp / email link to choose a password: `/parent/set-password?t=...` (first time) or `/parent/reset-password?t=...` (a reset).
 * The token is read from the address bar ONCE, the address is cleaned at once (history.replaceState via Location), the page asks the browser to send no referrer, and the token is only ever sent in a request BODY.
 * It is kept in a private field, never in a signal, a form, storage or a log. Every unusable link (wrong, used, expired) shows the same message.
 */
@Component({
  selector: 'app-parent-set-password',
  imports: [ReactiveFormsModule, RouterLink, ButtonComponent, IconComponent, InputComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../parent-pages.scss',
  template: `
    <div class="heading">
      <h2>{{ reset ? 'Choose a new password' : 'Choose your password' }}</h2>
      <p class="text-muted">{{ reset ? 'Enter a new password for your login.' : 'Set the password you will use to sign in.' }}</p>
    </div>

    @switch (state()) {
      @case ('checking') { <p role="status" data-state="checking">Checking your link…</p> }
      @case ('invalid') {
        <p class="alert" role="alert" data-state="invalid"><app-icon name="circle-alert" [size]="16" />This link is not valid any more. It may have been used already or have expired. Ask the school for a new one, or use “Forgot ID or password”.</p>
        <a class="link" routerLink="/parent/forgot" data-link="forgot">Forgot ID or password</a>
      }
      @case ('done') {
        <div class="ok" role="status" data-state="done">
          <p><strong>Your password is set.</strong></p>
          <p>Your login ID is <code data-field="login-id">{{ loginId() }}</code>. Keep it safe: you sign in with this ID and your new password.</p>
          <a class="button-link" routerLink="/login" data-link="login">Go to sign in</a>
        </div>
      }
      @default {
        <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          @if (error(); as message) { <p class="alert" role="alert" data-note="error"><app-icon name="circle-alert" [size]="16" />{{ message }}</p> }
          <app-input label="New password" type="password" prefixIcon="lock" formControlName="password" help="At least 8 characters." [errorText]="fieldError('password')" />
          <app-input label="Confirm password" type="password" prefixIcon="lock" formControlName="confirm" [errorText]="fieldError('confirm')" />
          <app-button type="submit" [block]="true" size="lg" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Set password' }}</app-button>
        </form>
      }
    }
  `,
})
export class ParentSetPasswordComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly meta = inject(Meta);
  private readonly api = inject(ParentAccountService);
  private readonly destroyRef = inject(DestroyRef);

  /** The one-time token. A private field on purpose: never rendered, never stored. */
  private token = '';
  protected reset = false;

  protected readonly state = signal<'checking' | 'ready' | 'invalid' | 'done'>('checking');
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly loginId = signal('');
  private readonly submitted = signal(false);

  protected readonly form = new FormGroup({
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(8), Validators.maxLength(100)] }),
    confirm: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  ngOnInit(): void {
    this.reset = this.route.snapshot.routeConfig?.path === 'parent/reset-password';
    // no referrer: the address (with the token) is never passed on to another site
    const tag = this.meta.addTag({ name: 'referrer', content: 'no-referrer' });
    this.destroyRef.onDestroy(() => { if (tag) this.meta.removeTagElement(tag); });

    this.token = (this.route.snapshot.queryParamMap.get('t') ?? '').trim();
    this.location.replaceState(this.location.path().split('?')[0]);   // the token leaves the address bar (and the history entry) immediately

    if (!this.token) { this.state.set('invalid'); return; }
    this.api.checkLink(this.token).subscribe({
      next: (r) => this.state.set(r.valid ? 'ready' : 'invalid'),
      error: () => this.state.set('ready'),   // could not check (offline?): let the parent try; the save gives the real answer
    });
  }

  protected fieldError(name: 'password' | 'confirm'): string | undefined {
    const c = this.form.controls[name];
    if (!(c.touched || this.submitted())) return undefined;
    if (name === 'password') return c.hasError('required') ? 'Enter a password' : c.invalid ? 'Use at least 8 characters' : undefined;
    if (c.hasError('required')) return 'Type the password again';
    return this.form.controls.password.value !== c.value ? 'The two passwords are not the same' : undefined;
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.error.set(null);
    this.form.markAllAsTouched();
    const { password, confirm } = this.form.getRawValue();
    if (this.form.invalid || password !== confirm) return;
    this.saving.set(true);
    this.api.setPassword(this.token, password).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (r) => { this.token = ''; this.loginId.set(r.loginId); this.form.reset(); this.state.set('done'); },
      error: (e: { error?: { message?: string }; status?: number }) => {
        this.form.controls.password.reset(''); this.form.controls.confirm.reset('');
        if (e?.status === 400 && /not valid any more/i.test(e.error?.message ?? '')) { this.token = ''; this.state.set('invalid'); return; }
        this.error.set(e?.status === 0 ? 'Cannot reach the server. Check your connection and try again.' : (e?.error?.message ?? 'The password could not be saved. Please try again.'));
      },
    });
  }
}
