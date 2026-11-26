import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent } from '../../../../shared/components';
import { ParentAccountResult, ParentAccountStatus, accountSummary, deliveryLabel } from '../../models/parent-account.model';
import { ParentAccountService } from '../../services/parent-account.service';

/**
 * A parent's account at a glance, for the staff who admit the child: has an account, has chosen a password, and how the last messages went, with "Resend link" (until the parent has chosen a password) and,
 * for the School Admin, "Send reset link" (after). Nobody here ever sees or sets a password or a link: the system sends it. Used on a confirmed admission and on the student's profile.
 */
@Component({
  selector: 'app-parent-account-card',
  imports: [ButtonComponent, CardComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    .row { display: flex; align-items: center; gap: var(--space-3); flex-wrap: wrap; }
    .meta { color: var(--color-text-muted); font-size: var(--text-sm); }
    ul { margin: var(--space-2) 0 0; padding-left: var(--space-5); font-size: var(--text-sm); }
    li.failed { color: var(--color-danger); }
  `],
  template: `
    <app-card title="Parent account">
      @if (loading()) {
        <p role="status">Loading…</p>
      } @else if (failed()) {
        <p role="alert" data-state="failed">The parent account could not be loaded. <app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></p>
      } @else if (status(); as s) {
        @if (!s.hasAccount) {
          <p data-state="none">{{ s.guardianName }} has no parent account yet.
            @if (!s.consentRecorded) { <span class="meta"> No parent consent is on record, so no account can be made.</span> }
            @else if (!s.whatsAppOnFile && !s.emailOnFile) { <span class="meta"> Add a WhatsApp number or an email on the Parent details screen first.</span> }
          </p>
        } @else {
          <div class="row" data-state="account">
            <span><strong>{{ s.guardianName }}</strong> · login ID <code data-field="login-id">{{ s.loginId }}</code></span>
            <span class="meta" data-field="password-state">{{ s.passwordSet ? 'Password chosen' : 'Has not chosen a password yet' }}</span>
          </div>
          <div class="row">
            @if (!s.passwordSet) {
              <app-button variant="secondary" size="sm" [disabled]="busy() || !canSend(s)" (click)="resend()" data-action="resend"><app-icon name="mail" [size]="14" />Resend link</app-button>
            } @else if (canReset()) {
              <app-button variant="secondary" size="sm" [disabled]="busy() || !canSend(s)" (click)="reset()" data-action="reset"><app-icon name="mail" [size]="14" />Send reset link</app-button>
            }
            <span class="meta" data-field="limit">{{ s.linksInLast24Hours }} of {{ s.maxLinksPer24Hours }} links sent in the last 24 hours</span>
          </div>
        }
        @if (s.deliveries.length > 0) {
          <ul aria-label="Recent messages" data-list="deliveries">
            @for (d of s.deliveries; track d.deliveryId) { <li [class.failed]="d.status === 'FAILED'">{{ label(d) }}</li> }
          </ul>
        }
      }
    </app-card>
  `,
})
export class ParentAccountCardComponent implements OnInit {
  private readonly api = inject(ParentAccountService);
  private readonly toast = inject(ToastService);

  readonly guardianId = input.required<number>();
  /** Only the School Admin may send a reset link. */
  readonly canReset = input<boolean>(false);

  protected readonly status = signal<ParentAccountStatus | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly label = deliveryLabel;
  protected readonly canSend = (s: ParentAccountStatus) => s.consentRecorded && s.linksInLast24Hours < s.maxLinksPer24Hours && (s.whatsAppOnFile || s.emailOnFile);
  protected readonly hasAccount = computed(() => this.status()?.hasAccount ?? false);

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.loading.set(true); this.failed.set(false);
    this.api.status(this.guardianId()).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (s) => this.status.set(s),
      error: () => this.failed.set(true),
    });
  }

  protected resend(): void { this.run(this.api.resend(this.guardianId())); }
  protected reset(): void { this.run(this.api.reset(this.guardianId())); }

  private run(request: ReturnType<ParentAccountService['resend']>): void {
    if (this.busy()) return;
    this.busy.set(true);
    request.pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (r: ParentAccountResult) => {
        const failed = r.deliveries.some((d) => d.status === 'FAILED');
        if (failed || r.deliveries.length === 0) this.toast.warning(accountSummary({ ...r, outcome: 'CREATED' }).text.replace('Parent account created', 'A new link was made'));
        else this.toast.success('A new link was sent to the parent.');
        this.load();
      },
      error: () => undefined,   // the server's reason (the limit, no consent, ...) is toasted by the interceptor
    });
  }
}
