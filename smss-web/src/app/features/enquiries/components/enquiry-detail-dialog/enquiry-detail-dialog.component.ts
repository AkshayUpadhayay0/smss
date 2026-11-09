import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent, ModalComponent, SelectComponent, SelectOption, TextareaComponent } from '../../../../shared/components';
import { ENQUIRY_STATUSES, EnquiryDetail, EnquiryStatus, STATUS_LABEL, TYPE_LABEL } from '../../models/enquiry-admin.model';
import { EnquiryService } from '../../services/enquiry.service';

/** Everything a visitor submitted, plus the Super Admin's status and notes. Closing or spamming asks for confirmation first. */
@Component({
  selector: 'app-enquiry-detail-dialog',
  imports: [DatePipe, ReactiveFormsModule, ModalComponent, ButtonComponent, IconComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './enquiry-detail-dialog.component.scss',
  template: `
    <app-modal [title]="title()" [dismissible]="!saving()" (closed)="closed.emit()">
      @if (loading()) {
        <p class="state">Loading…</p>
      } @else if (failed()) {
        <p class="state" role="alert">This enquiry could not be loaded.</p>
      } @else if (e(); as d) {
        <div class="actions">
          <a class="link" [href]="mailto()"><app-icon name="mail" [size]="16" /> Email</a>
          @if (tel(); as t) {
            <a class="link" [href]="t"><app-icon name="phone" [size]="16" /> Call</a>
          }
        </div>

        <dl class="facts">
          <div><dt>Received</dt><dd>{{ d.createdAt | date: 'dd MMM yyyy, h:mm a' }}</dd></div>
          <div><dt>Type</dt><dd>{{ typeLabel(d.enquiryType) }}</dd></div>
          <div><dt>Name</dt><dd>{{ d.fullName }}</dd></div>
          @if (d.designation) { <div><dt>Designation</dt><dd>{{ d.designation }}</dd></div> }
          @if (d.schoolName) { <div><dt>School</dt><dd>{{ d.schoolName }}</dd></div> }
          <div><dt>Email</dt><dd>{{ d.email }}</dd></div>
          @if (d.phone) { <div><dt>Phone</dt><dd>{{ d.phone }}</dd></div> }
          @if (d.city) { <div><dt>City</dt><dd>{{ d.city }}</dd></div> }
          @if (d.studentCountRange) { <div><dt>Students</dt><dd>{{ d.studentCountRange }}</dd></div> }
          @if (d.preferredDemoDate) { <div><dt>Preferred date</dt><dd>{{ d.preferredDemoDate | date: 'dd MMM yyyy' }}</dd></div> }
          @if (d.preferredTimeSlot) { <div><dt>Preferred time</dt><dd>{{ d.preferredTimeSlot }}</dd></div> }
          @if (d.subject) { <div class="wide"><dt>Subject</dt><dd>{{ d.subject }}</dd></div> }
          <div><dt>Consent given</dt><dd>{{ d.consentGiven ? 'Yes' : 'No' }}</dd></div>
          @if (d.handledByName) { <div><dt>Last handled by</dt><dd>{{ d.handledByName }}</dd></div> }
        </dl>

        @if (d.message) {
          <h4>Message</h4>
          <p class="message">{{ d.message }}</p>
        }

        <div class="edit">
          <app-select label="Status" [formControl]="status" [options]="statusOptions" />
          <app-textarea label="Admin notes" [rows]="4" [maxLength]="4000" [formControl]="notes" helpText="Only the Super Admin sees these notes." />
        </div>
      }

      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Close</app-button>
        @if (e()) {
          <app-button [disabled]="saving() || !dirty()" (click)="save()">{{ saving() ? 'Saving…' : 'Save changes' }}</app-button>
        }
      </ng-container>
    </app-modal>
  `,
})
export class EnquiryDetailDialogComponent implements OnInit {
  private readonly service = inject(EnquiryService);
  private readonly confirm = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);

  readonly enquiryId = input.required<number>();
  readonly closed = output<void>();
  /** Emitted after a successful save so the list can refresh. */
  readonly changed = output<void>();

  protected readonly e = signal<EnquiryDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly saving = signal(false);

  protected readonly status = new FormControl<string>('NEW', { nonNullable: true });
  protected readonly notes = new FormControl<string>('', { nonNullable: true });
  private readonly formVersion = signal(0);

  protected readonly statusOptions: SelectOption[] = ENQUIRY_STATUSES.map((s) => ({ value: s, label: STATUS_LABEL[s] }));
  protected readonly title = computed(() => (this.e() ? `Enquiry from ${this.e()!.fullName}` : 'Enquiry'));
  protected readonly dirty = computed(() => {
    this.formVersion();
    const d = this.e();
    return !!d && (this.status.value !== d.status || this.notes.value.trim() !== (d.adminNotes ?? '').trim());
  });
  protected readonly mailto = computed(() => {
    const d = this.e();
    return d ? `mailto:${d.email}?subject=${encodeURIComponent('Re: ' + (d.subject || (d.enquiryType === 'DEMO' ? 'Your SMSS demo request' : 'Your message to SMSS')))}` : '';
  });
  protected readonly tel = computed(() => {
    const phone = this.e()?.phone;
    return phone ? `tel:${phone.replace(/[^\d+]/g, '')}` : null;
  });

  protected typeLabel(t: EnquiryDetail['enquiryType']): string {
    return TYPE_LABEL[t];
  }

  ngOnInit(): void {
    this.status.valueChanges.subscribe(() => this.formVersion.update((v) => v + 1));
    this.notes.valueChanges.subscribe(() => this.formVersion.update((v) => v + 1));
    this.service.get(this.enquiryId()).subscribe({
      next: (d) => {
        this.show(d);
        this.loading.set(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }

  protected async save(): Promise<void> {
    const d = this.e();
    if (!d || this.saving()) return;
    const next = this.status.value as EnquiryStatus;

    if (next !== d.status && (next === 'SPAM' || next === 'CLOSED')) {
      const ok = await this.confirm.confirm({
        title: next === 'SPAM' ? 'Mark as spam?' : 'Close this enquiry?',
        message:
          next === 'SPAM'
            ? `${d.fullName}'s enquiry will be marked as spam and no longer counted as a lead.`
            : `${d.fullName}'s enquiry will be marked as closed.`,
        confirmText: next === 'SPAM' ? 'Mark as spam' : 'Close enquiry',
        variant: 'danger',
      });
      if (!ok) return;
    }

    this.saving.set(true);
    this.service.updateStatus(d.enquiryId, next, this.notes.value).subscribe({
      next: (updated) => {
        this.show(updated);
        this.saving.set(false);
        this.toast.success('Enquiry updated.');
        this.changed.emit();
      },
      error: () => this.saving.set(false), // the global error toast has already explained
    });
  }

  private show(d: EnquiryDetail): void {
    this.e.set(d);
    this.status.setValue(d.status, { emitEvent: false });
    this.notes.setValue(d.adminNotes ?? '', { emitEvent: false });
    this.formVersion.update((v) => v + 1);
  }
}
