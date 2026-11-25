import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, ModalComponent, TextareaComponent } from '../../../../shared/components';
import { MergePreview, MergeResult } from '../../models/guardian.model';
import { GuardianService } from '../../services/guardian.service';

/**
 * Merge one parent record into another. The preview (it changes nothing) is read first and shown in full: which students' links move, which are combined, the contact copied, the applications repointed and
 * the login. The merge is only sent after the admin ticks that they have read it. The absorbed record is kept (deactivated and pointed at the survivor); nothing is deleted. School Admin and Principal only.
 */
@Component({
  selector: 'app-merge-dialog',
  imports: [ReactiveFormsModule, ButtonComponent, ModalComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal title="Merge parent records" [dismissible]="!busy()" (closed)="closed.emit()">
      @if (failed()) {
        <p class="note" role="alert">The preview could not be loaded.</p>
      } @else if (!preview()) {
        <p class="note" role="status">Loading the preview…</p>
      } @else {
        @let p = preview()!;
        <p class="lead" data-note="summary">
          <strong>{{ p.absorbed.name }}</strong> ({{ p.absorbed.mobileNumber }}) will be merged into <strong>{{ p.survivor.name }}</strong> ({{ p.survivor.mobileNumber }}).
          {{ p.absorbed.name }} is kept as an inactive record and is not deleted.
        </p>
        @for (b of p.blockers; track b) {
          <p class="blocker" role="alert" data-blocker>{{ b }}</p>
        }
        <h4>Students</h4>
        <ul data-list="links">
          @for (l of p.links; track l.studentId) {
            <li>{{ l.studentName }} <span class="meta">· {{ l.admissionNumber }}{{ l.className ? ' · ' + l.className + (l.sectionName ? ' – ' + l.sectionName : '') : '' }}</span>
              — {{ l.action === 'MOVE' ? 'now linked to ' + p.survivor.name : 'already linked to ' + p.survivor.name + ': the two links are combined (primary wins, portal access stays on)' }}</li>
          } @empty {
            <li class="meta">No student is linked to this record.</li>
          }
        </ul>
        <h4>Contact copied to {{ p.survivor.name }}</h4>
        <ul data-list="fields">
          @for (f of fieldNames(p); track f) {
            <li>{{ label(f) }}: {{ p.fieldsToCopy[f] }}</li>
          } @empty {
            <li class="meta">Nothing: {{ p.survivor.name }} already has these. A stored mobile number is never copied.</li>
          }
        </ul>
        @if (p.applications.length > 0) {
          <h4>Applications</h4>
          <ul data-list="applications">
            @for (a of p.applications; track a.admissionId + a.role) {
              <li>{{ a.applicationNumber }} ({{ a.role === 'PRIMARY' ? 'primary parent' : 'extra parent' }}): {{ a.action === 'REPOINT' ? 'moves to ' + p.survivor.name : 'already has ' + p.survivor.name + ', this entry is dropped' }}</li>
            }
          </ul>
        }
        @if (p.guardiansToRepoint.length > 0) {
          <p class="meta" data-note="repoint">{{ p.guardiansToRepoint.length }} record(s) merged into {{ p.absorbed.name }} earlier now point at {{ p.survivor.name }}.</p>
        }
        @if (p.account === 'MOVE') {
          <p class="meta" data-note="account">{{ p.absorbed.name }} has a login: it moves to {{ p.survivor.name }}.</p>
        }
        <app-textarea label="Reason (optional)" [maxLength]="500" [formControl]="reason" />
        @if (p.canMerge) {
          <label class="ack"><input type="checkbox" [checked]="ack()" (change)="ack.set(!ack())" data-field="ack" /> I have read the preview and want to merge these two records.</label>
        }
      }
      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="busy()" (click)="closed.emit()">Cancel</app-button>
        <app-button variant="danger" [disabled]="busy() || !preview()?.canMerge || !ack()" (click)="merge()" data-action="merge">{{ busy() ? 'Merging…' : 'Merge' }}</app-button>
      </ng-container>
    </app-modal>
  `,
  styles: `
    .lead { margin: 0 0 var(--space-3); }
    h4 { margin: var(--space-3) 0 var(--space-1); font-size: var(--text-sm); }
    ul { margin: 0; padding-left: var(--space-5); font-size: var(--text-sm); }
    .meta, .note { font-size: var(--text-xs); color: var(--color-text-muted); }
    .blocker { padding: var(--space-2) var(--space-3); border-radius: var(--radius-md); background: #fef2f2; color: #991b1b; font-size: var(--text-sm); }
    .ack { display: flex; gap: var(--space-2); align-items: center; margin-top: var(--space-3); font-size: var(--text-sm); }
  `,
})
export class MergeDialogComponent implements OnInit {
  private readonly service = inject(GuardianService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly survivorId = input.required<number>();
  readonly absorbedId = input.required<number>();
  readonly merged = output<MergeResult>();
  readonly closed = output<void>();

  protected readonly preview = signal<MergePreview | null>(null);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly ack = signal(false);
  protected readonly reason = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] });

  protected fieldNames(p: MergePreview): string[] {
    return Object.keys(p.fieldsToCopy);
  }

  protected label(field: string): string {
    return field === 'whatsapp_number' ? 'WhatsApp number' : field === 'email' ? 'Email' : 'Occupation';
  }

  ngOnInit(): void {
    this.service
      .mergePreview(this.survivorId(), this.absorbedId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (p) => this.preview.set(p), error: () => this.failed.set(true) });
  }

  protected merge(): void {
    if (this.busy() || !this.ack() || !this.preview()?.canMerge) return;
    this.busy.set(true);
    this.service
      .merge({ survivorId: this.survivorId(), absorbedId: this.absorbedId(), confirm: true, reason: this.reason.value.trim() || null })
      .pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.toast.success('The parent records were merged.');
          this.merged.emit(r);
        },
        error: () => undefined,   // the server reason is toasted by the interceptor (e.g. the pair was merged meanwhile)
      });
  }
}
