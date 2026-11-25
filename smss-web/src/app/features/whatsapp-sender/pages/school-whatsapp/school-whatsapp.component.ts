import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { Breadcrumb, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { whatsAppValidator } from '../../../../core/utils/whatsapp';
import { SENDER_STATUSES, SenderStatus, SenderStatusModel } from '../../models/whatsapp-sender.model';
import { WhatsAppSenderService } from '../../services/whatsapp-sender.service';

/**
 * Super Admin: set a school's WhatsApp number, the Meta phone-number id and the sender status (Not registered, Pending, Active). Active needs the phone-number id and records when it was verified. Nothing is
 * sent from here and no token or secret is ever typed or shown: tokens will live only in user-secrets and environment variables on the server.
 */
@Component({
  selector: 'app-school-whatsapp',
  imports: [ReactiveFormsModule, DatePipe, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header [title]="'WhatsApp sender' + (current() ? ' · ' + current()!.schoolName : '')" subtitle="Which WhatsApp number this school's messages go from" icon="smartphone" [breadcrumbs]="breadcrumbs()">
      <app-button variant="secondary" (click)="back()"><app-icon name="arrow-left" [size]="16" />Back to school</app-button>
    </app-page-header>

    @if (loading()) {
      <app-card><p class="note" role="status">Loading…</p></app-card>
    } @else if (loadFailed()) {
      <app-card><p class="note" role="alert">This school could not be loaded.</p></app-card>
    } @else {
      <app-card title="Sender">
        <p class="note" data-note="sends-from">{{ current()!.sendsFrom }}.</p>
        <form [formGroup]="form" class="grid" (ngSubmit)="save()" novalidate>
          <app-select label="Status" [required]="true" [options]="statusOptions" formControlName="status" [errorText]="err('status')" />
          <app-input label="School WhatsApp number" type="tel" placeholder="10 digits or +91 98765 43210" formControlName="whatsAppNumber" [errorText]="err('whatsAppNumber')" />
          <app-input label="Meta phone number id" placeholder="Digits only (required for Active)" formControlName="phoneNumberId" [errorText]="err('phoneNumberId')" />
        </form>
        <p class="note" data-note="rules">Pending and Active need the WhatsApp number. Active also needs the Meta phone number id and records the day it was verified. No token or secret is kept here.</p>
        <div class="meta" data-note="audit">
          @if (current()!.verifiedAt) { <span>Verified {{ current()!.verifiedAt | date: 'dd MMM yyyy' }}. </span> }
          Last saved {{ current()!.updatedAt | date: 'dd MMM yyyy, HH:mm' }}{{ current()!.updatedByName ? ' by ' + current()!.updatedByName : '' }}.
        </div>
        <div class="footer"><app-button [disabled]="saving()" (click)="save()">{{ saving() ? 'Saving…' : 'Save' }}</app-button></div>
      </app-card>
    }
  `,
  styles: `
    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: var(--space-4); }
    .note, .meta { font-size: var(--text-sm); color: var(--color-text-muted); }
    .footer { display: flex; justify-content: flex-end; margin-top: var(--space-4); }
  `,
})
export class SchoolWhatsAppComponent implements OnInit {
  private readonly service = inject(WhatsAppSenderService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /** Bound from the :schoolId route parameter. */
  readonly schoolId = input.required<string>();

  protected readonly statusOptions: SelectOption[] = SENDER_STATUSES.map((s) => ({ label: s.label, value: s.value }));
  protected readonly current = signal<SenderStatusModel | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);
  protected readonly breadcrumbs = computed<Breadcrumb[]>(() => [{ label: 'Registered Schools', link: '/schools' }, { label: this.current()?.schoolName ?? 'School', link: `/schools/${this.schoolId()}/view` }, { label: 'WhatsApp sender' }]);

  protected readonly form = new FormGroup({
    status: new FormControl<SenderStatus>('NOT_REGISTERED', { nonNullable: true, validators: [Validators.required] }),
    whatsAppNumber: new FormControl('', { nonNullable: true, validators: [(c) => (String(c.value ?? '').trim() === '' ? null : whatsAppValidator(c))] }),
    phoneNumberId: new FormControl('', { nonNullable: true, validators: [Validators.pattern(/^\s*([0-9]{5,50})?\s*$/), Validators.maxLength(50)] }),
  });

  ngOnInit(): void {
    this.service
      .get(this.schoolId())
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (s) => this.apply(s), error: () => this.loadFailed.set(true) });
  }

  private apply(s: SenderStatusModel): void {
    this.current.set(s);
    this.form.patchValue({ status: s.status, whatsAppNumber: s.whatsAppNumber ?? '', phoneNumberId: s.phoneNumberId ?? '' });
  }

  protected err(name: 'status' | 'whatsAppNumber' | 'phoneNumberId'): string | undefined {
    const c = this.form.controls[name];
    if (!(c.touched || this.submitted()) || c.valid) return undefined;
    if (name === 'phoneNumberId') return 'The phone number id is a number of 5 to 50 digits.';
    return (c.errors?.['pattern'] as string) ?? 'Enter a valid value.';
  }

  protected back(): void {
    void this.router.navigate(['/schools', this.schoolId(), 'view']);
  }

  protected save(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    if (v.status !== 'NOT_REGISTERED' && !v.whatsAppNumber.trim()) { this.toast.error(`A WhatsApp number is required for the status ${v.status}.`); return; }
    if (v.status === 'ACTIVE' && !v.phoneNumberId.trim()) { this.toast.error('Active needs the Meta phone number id.'); return; }
    this.saving.set(true);
    this.service
      .save(this.schoolId(), { status: v.status, whatsAppNumber: v.whatsAppNumber.trim() || null, phoneNumberId: v.phoneNumberId.trim() || null })
      .pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (s) => {
          this.apply(s);
          this.toast.success('WhatsApp sender saved.');
        },
        error: () => undefined,   // the server's reason is toasted by the interceptor; the form keeps its values
      });
  }
}
