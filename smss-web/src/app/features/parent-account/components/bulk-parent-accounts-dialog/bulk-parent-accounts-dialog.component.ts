import { ChangeDetectionStrategy, Component, OnInit, inject, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ButtonComponent, IconComponent, ModalComponent } from '../../../../shared/components';
import { BulkParentAccountPreview, BulkParentAccountResult } from '../../models/parent-account.model';
import { ParentAccountService } from '../../services/parent-account.service';

/**
 * Creates parent accounts for existing guardians (School Admin). Shows how many guardians qualify first (complete details: a WhatsApp number AND an email, linked to an Active student, no account yet), creates
 * at most one run's worth (100) and never runs by itself. The tick is the School Admin's confirmation that the SCHOOL HOLDS the parents' consent; it is recorded (with who and when) for every guardian in the run that has none.
 */
@Component({
  selector: 'app-bulk-parent-accounts-dialog',
  imports: [ModalComponent, ButtonComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    dl { display: grid; grid-template-columns: 1fr auto; gap: var(--space-2) var(--space-4); margin: 0 0 var(--space-4); }
    dd { margin: 0; font-weight: 600; text-align: right; }
    .consent { display: flex; gap: var(--space-2); align-items: flex-start; font-size: var(--text-sm); }
    ul { max-height: 220px; overflow: auto; padding-left: var(--space-5); font-size: var(--text-sm); }
    .bad { color: var(--color-danger); }
  `],
  template: `
    <app-modal title="Create parent accounts" [dismissible]="!saving()" (closed)="closed.emit()">
      @if (result(); as r) {
        <p role="status" data-state="result"><strong>{{ r.created }}</strong> account(s) created{{ r.notCreated > 0 ? ', ' + r.notCreated + ' not created' : '' }}.
          @if (r.remaining > 0) { {{ r.remaining }} more are waiting: run it again. }</p>
        <p class="hint">Each parent was sent a link to choose their own password. Nobody, including you, can see it.</p>
        @if (problems().length > 0) {
          <ul aria-label="Not created" data-list="problems">
            @for (i of problems(); track i.guardianId) { <li class="bad">{{ i.name }}: {{ i.reason }}</li> }
          </ul>
        }
      } @else if (loadFailed()) {
        <p role="alert" data-state="failed">The count could not be loaded. <app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></p>
      } @else if (preview(); as p) {
        <dl data-section="preview">
          <dt>Ready to get an account</dt><dd data-field="eligible">{{ p.eligible }}</dd>
          <dt>This run will create</dt><dd data-field="will-create">{{ p.willCreate }} (at most {{ p.maxPerRun }} per run)</dd>
          <dt>Skipped: details still incomplete</dt><dd data-field="incomplete">{{ p.incompleteDetails }}</dd>
          <dt>Ready, with no consent on record yet</dt><dd data-field="no-consent">{{ p.withoutConsentOnRecord }}</dd>
        </dl>
        @if (p.incompleteDetails > 0) { <p class="hint">Complete the missing WhatsApp numbers and emails on the “Complete parent details” screen first.</p> }
        @if (p.eligible > 0) {
          <label class="consent"><input type="checkbox" [checked]="confirmed()" (change)="confirmed.set(!confirmed())" data-field="consent" />
            <span>I confirm that this school holds the parents' consent to be contacted by WhatsApp and email about their account and their children. This confirmation is recorded against each parent with my name and today's date.</span></label>
        }
      } @else {
        <p role="status">Loading…</p>
      }
      <ng-container modal-footer>
        @if (result()) {
          <app-button (click)="done.emit()" data-action="done">Done</app-button>
        } @else {
          <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
          <app-button [disabled]="saving() || !confirmed() || !(preview() && preview()!.eligible > 0)" (click)="create()" data-action="create"><app-icon name="users" [size]="14" />{{ saving() ? 'Creating…' : 'Create accounts' }}</app-button>
        }
      </ng-container>
    </app-modal>
  `,
})
export class BulkParentAccountsDialogComponent implements OnInit {
  private readonly api = inject(ParentAccountService);

  readonly closed = output<void>();
  readonly done = output<void>();

  protected readonly preview = signal<BulkParentAccountPreview | null>(null);
  protected readonly loadFailed = signal(false);
  protected readonly confirmed = signal(false);
  protected readonly saving = signal(false);
  protected readonly result = signal<BulkParentAccountResult | null>(null);
  protected problems() { return (this.result()?.items ?? []).filter((i) => i.outcome !== 'CREATED' && i.outcome !== 'CHILD_ADDED'); }

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.loadFailed.set(false);
    this.api.bulkPreview().subscribe({ next: (p) => this.preview.set(p), error: () => this.loadFailed.set(true) });
  }

  protected create(): void {
    if (this.saving() || !this.confirmed()) return;
    this.saving.set(true);
    this.api.bulkCreate(true).pipe(finalize(() => this.saving.set(false))).subscribe({ next: (r) => this.result.set(r), error: () => undefined });
  }
}
