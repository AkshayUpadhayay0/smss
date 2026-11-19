import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { extractErrorMessage } from '../../../../core/interceptors/error.interceptor';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent } from '../../../../shared/components';
import { FeeReminderPreview, MAX_GRACE_DAYS, NotificationSettings, SEND_TIME } from '../../models/notification-settings.model';
import { NotificationSettingsService } from '../../services/notification-settings.service';

/** A whole number of days: digits only (the server takes an integer). */
const wholeNumber = (c: { value: unknown }): ValidationErrors | null => (/^\d+$/.test(String(c.value ?? '').trim()) ? null : { whole: true });

/** The server's own reason for a refusal (4xx: it says what is wrong or who may do this); a plain message for a server error or anything unexpected. */
function failureMessage(e: unknown, fallback: string): string {
  return e instanceof HttpErrorResponse && e.status >= 400 && e.status < 500 ? extractErrorMessage(e) : fallback;
}

/**
 * School Setup → Notification Settings (School Admin only): the daily fee-reminder push. Students with an overdue fee get one notification a day on the
 * school app until the dues are paid. The message never shows the amount. "Preview today" is a dry run that sends nothing.
 */
@Component({
  selector: 'app-notification-settings',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './notification-settings.component.scss',
  templateUrl: './notification-settings.component.html',
})
export class NotificationSettingsComponent implements OnInit {
  private readonly service = inject(NotificationSettingsService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'School Setup' }, { label: 'Notification Settings' }];
  protected readonly maxGrace = MAX_GRACE_DAYS;

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  /** The server refused the load (403): retrying cannot help, so the screen says so instead of offering Try again. */
  protected readonly forbidden = signal(false);
  protected readonly saving = signal(false);

  /** What the server has (the baseline for "unsaved changes"). */
  private readonly saved = signal<NotificationSettings | null>(null);
  protected readonly lastRunDate = computed(() => this.saved()?.lastFeeReminderRunDate ?? null);

  protected readonly form = new FormGroup({
    feeReminderEnabled: new FormControl(false, { nonNullable: true }),
    feeReminderSendTime: new FormControl('09:00', { nonNullable: true, validators: [Validators.required, Validators.pattern(SEND_TIME)] }),
    feeReminderGraceDays: new FormControl('0', {
      nonNullable: true,
      validators: [Validators.required, wholeNumber, Validators.min(0), Validators.max(MAX_GRACE_DAYS)],
    }),
  });
  /** The form's value and validity as signals (a computed() cannot see `form.valid` or a reset made with emitEvent: false). */
  protected readonly value = signal(this.form.getRawValue());
  private readonly formValid = signal(true);
  private syncForm(): void {
    this.value.set(this.form.getRawValue());
    this.formValid.set(this.form.valid);
  }

  protected readonly dirty = computed(() => {
    const s = this.saved();
    if (!s) return false;
    const v = this.value();
    return (
      v.feeReminderEnabled !== s.feeReminderEnabled ||
      v.feeReminderSendTime.trim() !== s.feeReminderSendTime ||
      v.feeReminderGraceDays.trim() !== String(s.feeReminderGraceDays)
    );
  });
  protected readonly canSave = computed(() => this.dirty() && this.formValid() && !this.saving());

  // ── preview (dry run) ────────────────────────────────────────────────
  protected readonly preview = signal<FeeReminderPreview | null>(null);
  protected readonly previewing = signal(false);
  protected readonly previewError = signal<string | null>(null);

  ngOnInit(): void {
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.syncForm());
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.forbidden.set(false);
    this.service
      .get()
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (s) => this.apply(s),
        error: (e: unknown) => {
          if (e instanceof HttpErrorResponse && e.status === 403) this.forbidden.set(true);
          else this.loadFailed.set(true);
        },
      });
  }

  private apply(s: NotificationSettings): void {
    this.saved.set(s);
    this.form.reset(
      { feeReminderEnabled: s.feeReminderEnabled, feeReminderSendTime: s.feeReminderSendTime, feeReminderGraceDays: String(s.feeReminderGraceDays) },
      { emitEvent: false },
    );
    this.syncForm();
  }

  protected save(): void {
    if (!this.canSave()) return;
    const v = this.form.getRawValue();
    this.saving.set(true);
    this.service
      .save({ feeReminderEnabled: v.feeReminderEnabled, feeReminderSendTime: v.feeReminderSendTime.trim(), feeReminderGraceDays: Number(v.feeReminderGraceDays.trim()) })
      .pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (s) => {
          this.apply(s);
          this.preview.set(null); // it described the old settings
          this.toast.success(s.feeReminderEnabled ? 'Notification settings saved. Fee reminders are on.' : 'Notification settings saved. Fee reminders are off.');
        },
        error: (e: unknown) => this.toast.error(failureMessage(e, 'The settings could not be saved. Please try again.')), // the form keeps what was typed
      });
  }

  protected runPreview(): void {
    if (this.previewing()) return;
    this.previewing.set(true);
    this.previewError.set(null);
    this.service
      .previewFeeReminders()
      .pipe(finalize(() => this.previewing.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (p) => this.preview.set(p),
        error: (e: unknown) => {
          this.preview.set(null);
          this.previewError.set(failureMessage(e, 'The preview could not be loaded. Please try again.'));
        },
      });
  }

  protected err(name: 'feeReminderSendTime' | 'feeReminderGraceDays'): string | undefined {
    const c = this.form.controls[name];
    if (!c.errors) return undefined;
    if (name === 'feeReminderSendTime') return 'Enter a time like 09:00 (24-hour, India time).';
    if (c.errors['required'] || c.errors['whole']) return `Enter a whole number of days, from 0 to ${MAX_GRACE_DAYS}.`;
    return `The grace period must be between 0 and ${MAX_GRACE_DAYS} days.`;
  }
}
