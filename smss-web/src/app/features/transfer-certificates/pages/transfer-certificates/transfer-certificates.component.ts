import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, PageHeaderComponent } from '../../../../shared/components';
import { TcCheck, TransferCertificate } from '../../models/transfer-certificate.model';
import { TransferCertificateService } from '../../services/transfer-certificate.service';

/**
 * Transfer certificates (School Admin and Principal). Issue one for a student whose leaving is recorded (left, transferred out or passed out), list them, download the original, print a duplicate
 * (same number, marked as a duplicate) and void a wrong one with a reason. The server decides everything (who may, what blocks it, the number): this page only asks and shows the answer.
 */
@Component({
  selector: 'app-transfer-certificates',
  imports: [CurrencyPipe, DatePipe, ButtonComponent, CardComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header title="Transfer certificates" subtitle="Issue, download, duplicate and void transfer certificates" icon="file-text" [breadcrumbs]="[{ label: 'Students', link: '/students' }, { label: 'Transfer certificates' }]" />

    <app-card title="Issue a certificate">
      <p class="hint">A certificate can be issued only for a student whose leaving has been recorded (left, transferred out or passed out). Enter the student id and check.</p>
      <div class="row">
        <label class="field"><span>Student id</span><input data-student [value]="studentId()" (input)="studentId.set($any($event.target).value); reset()" autocomplete="off" /></label>
        <app-button data-check-btn variant="secondary" [disabled]="!studentId().trim() || busy()" (click)="check()">Check</app-button>
      </div>
      @if (checkFailed()) { <p class="issue" role="alert" data-check-failed>That could not be checked. Please try again.</p> }
      @if (check_(); as c) {
        <div data-check>
          <p><strong>{{ c.studentName }}</strong> ({{ c.studentId }}) · {{ c.className }} {{ c.sectionName }} · {{ c.yearName }} · {{ c.outcome ?? 'no leaving recorded' }}@if (c.outcome) { · left on {{ c.leavingDate | date: 'd MMM yyyy' }} }</p>
          @if (c.blockers.length) { <ul class="issue" data-blockers>@for (b of c.blockers; track b) { <li>{{ b }}</li> }</ul> }
          @if (c.canIssue) {
            @if (c.requiresOverride) {
              <p class="issue" data-dues>Unpaid dues up to the leaving date: {{ c.outstanding | currency: 'INR' : 'symbol' : '1.0-0' }}. The certificate can still be issued with a reason; the certificate itself only says "Fee dues: Not cleared".</p>
              <label class="check"><input type="checkbox" data-override [checked]="overrideDues()" (change)="overrideDues.set($any($event.target).checked)" /> Issue anyway</label>
              <label class="field wide"><span>Reason (at least 10 characters, kept on record, not printed)</span><textarea data-override-reason rows="2" maxlength="500" [value]="overrideReason()" (input)="overrideReason.set($any($event.target).value)"></textarea></label>
            }
            <label class="field"><span>Conduct</span><input data-conduct maxlength="100" [value]="conduct()" (input)="conduct.set($any($event.target).value)" /></label>
            <label class="field wide"><span>Remarks (optional)</span><textarea data-remarks rows="2" maxlength="500" [value]="remarks()" (input)="remarks.set($any($event.target).value)"></textarea></label>
            <app-button data-issue-btn [disabled]="busy() || (c.requiresOverride && (!overrideDues() || overrideReason().trim().length < 10))" (click)="issue(c)">{{ busy() ? 'Issuing…' : 'Issue certificate' }}</app-button>
          }
        </div>
      }
    </app-card>

    <app-card title="Issued certificates">
      <div class="row">
        <label class="field"><span>Show</span>
          <select data-filter (change)="status.set($any($event.target).value); load()">
            <option value="">All</option><option value="ISSUED">Issued</option><option value="VOID">Void</option>
          </select></label>
      </div>
      @if (loading()) { <p role="status">Loading…</p> }
      @else if (failed()) { <p class="issue" role="alert" data-failed>The list could not be loaded. Please try again.</p> }
      @else if (list().length === 0) { <p data-empty>No certificates yet.</p> }
      @else {
        <div class="scroll">
          <table class="rows" data-rows>
            <thead><tr><th>Number</th><th>Student</th><th>Outcome</th><th>Issued</th><th>Status</th><th>Actions</th></tr></thead>
            <tbody>
              @for (t of list(); track t.certificateId) {
                <tr [attr.data-row]="t.certificateNumber">
                  <td>{{ t.certificateNumber }}</td>
                  <td>{{ t.studentName }}<small>{{ t.studentId }} · {{ t.className }} {{ t.sectionName }}</small></td>
                  <td>{{ t.outcome }}</td>
                  <td>{{ t.issuedAt | date: 'd MMM yyyy' }}<small>{{ t.issuedBy }}</small></td>
                  <td>{{ t.status === 'VOID' ? 'Void' : 'Issued' }}@if (t.duplicates) { <small>{{ t.duplicates }} duplicate{{ t.duplicates === 1 ? '' : 's' }}</small> }@if (t.status === 'VOID') { <small>{{ t.voidReason }}</small> }</td>
                  <td class="actions">
                    @if (t.status === 'ISSUED') {
                      <app-button size="sm" variant="secondary" data-download (click)="download(t, false)">Download</app-button>
                      <app-button size="sm" variant="secondary" data-duplicate (click)="download(t, true)">Duplicate</app-button>
                      <app-button size="sm" variant="secondary" data-void-open (click)="voiding.set(voiding() === t.certificateId ? null : t.certificateId); voidReason.set('')">Void</app-button>
                    }
                    @if (voiding() === t.certificateId) {
                      <div class="void">
                        <textarea data-void-reason rows="2" maxlength="500" placeholder="Reason (at least 10 characters)" [value]="voidReason()" (input)="voidReason.set($any($event.target).value)"></textarea>
                        <app-button data-void-confirm [disabled]="voidReason().trim().length < 10 || busy()" (click)="doVoid(t)">Void this certificate</app-button>
                      </div>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </app-card>
  `,
  styles: `
    .hint { margin: 0 0 var(--space-3); font-size: var(--text-sm); color: var(--color-text-muted); }
    .row { display: flex; flex-wrap: wrap; gap: var(--space-4); align-items: flex-end; margin-bottom: var(--space-3); }
    .field { display: flex; flex-direction: column; gap: var(--space-1); font-size: var(--text-sm); min-width: 200px; }
    .wide { min-width: 100%; textarea { width: 100%; } }
    .check { display: flex; gap: var(--space-2); align-items: center; font-size: var(--text-sm); margin: var(--space-2) 0; }
    .issue { color: #991b1b; margin: var(--space-2) 0; }
    .scroll { overflow-x: auto; }
    .rows { width: 100%; border-collapse: collapse; font-size: var(--text-sm); }
    .rows th, .rows td { text-align: left; padding: var(--space-2) var(--space-3); border-bottom: 1px solid var(--color-border); vertical-align: top; }
    small { display: block; color: var(--color-text-muted); }
    .actions { display: flex; flex-wrap: wrap; gap: var(--space-2); }
    .void { width: 100%; display: flex; flex-direction: column; gap: var(--space-2); }
  `,
})
export class TransferCertificatesComponent implements OnInit {
  private readonly api = inject(TransferCertificateService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly studentId = signal('');
  protected readonly check_ = signal<TcCheck | null>(null);
  protected readonly checkFailed = signal(false);
  protected readonly overrideDues = signal(false);
  protected readonly overrideReason = signal('');
  protected readonly conduct = signal('Good');
  protected readonly remarks = signal('');
  protected readonly busy = signal(false);

  protected readonly list = signal<TransferCertificate[]>([]);
  protected readonly status = signal('');
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly voiding = signal<number | null>(null);
  protected readonly voidReason = signal('');

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api.list(this.status() || undefined).pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: (l) => this.list.set(l), error: () => this.failed.set(true) });
  }

  protected reset(): void { this.check_.set(null); this.checkFailed.set(false); this.overrideDues.set(false); this.overrideReason.set(''); }

  protected check(): void {
    if (!this.studentId().trim() || this.busy()) return;
    this.busy.set(true);
    this.reset();
    this.api.check(this.studentId().trim()).pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (c) => { this.check_.set(c); this.conduct.set(c.suggestedConduct || 'Good'); },
      error: () => this.checkFailed.set(true),
    });
  }

  protected async issue(c: TcCheck): Promise<void> {
    if (this.busy()) return;
    const ok = await this.confirmDialog.confirm({ title: 'Issue this certificate?', message: `A certificate number is used up for ${c.studentName}. A wrong one can only be voided, never reused.`, confirmText: 'Issue', variant: 'primary' });
    if (!ok) return;
    this.busy.set(true);
    this.api
      .issue({ studentId: c.studentId, conduct: this.conduct().trim() || undefined, remarks: this.remarks().trim() || undefined, confirm: true, overrideDues: c.requiresOverride ? this.overrideDues() : undefined,
               overrideReason: c.requiresOverride ? this.overrideReason().trim() : undefined })
      .pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (t) => { this.toast.success(`Certificate ${t.certificateNumber} is issued.`); this.reset(); this.remarks.set(''); this.studentId.set(''); this.load(); },
        error: () => undefined,   // the server's plain reason is toasted; nothing was issued
      });
  }

  protected download(t: TransferCertificate, duplicate: boolean): void {
    const source: Observable<Blob> = duplicate ? this.api.duplicate(t.certificateId) : this.api.original(t.certificateId);
    source.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `Transfer-certificate-${t.certificateNumber.replace(/[^A-Za-z0-9-]/g, '_')}${duplicate ? '-duplicate' : ''}.pdf`;
        a.click();
        URL.revokeObjectURL(url);
        if (duplicate) this.load();   // the duplicate count changed
      },
      error: () => this.toast.warning('The certificate could not be downloaded. Please try again.'),
    });
  }

  protected async doVoid(t: TransferCertificate): Promise<void> {
    if (this.voidReason().trim().length < 10 || this.busy()) return;
    const ok = await this.confirmDialog.confirm({ title: 'Void this certificate?', message: `${t.certificateNumber} will print nothing from now on and its number stays used. This cannot be undone.`, confirmText: 'Void it', variant: 'danger' });
    if (!ok) return;
    this.busy.set(true);
    this.api.void(t.certificateId, this.voidReason().trim()).pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.toast.success('The certificate is void.'); this.voiding.set(null); this.voidReason.set(''); this.load(); },
      error: () => undefined,
    });
  }
}
