import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, SelectComponent, SelectOption, TextareaComponent } from '../../../../shared/components';
import { MAX_BODY, MAX_TITLE, Notice, NoticeAudience } from '../../models/notice.model';
import { NoticeService } from '../../services/notice.service';
import { AUDIENCE_LABELS } from '../../utils/notice-format';

const notBlank = (c: AbstractControl): ValidationErrors | null => (String(c.value ?? '').trim() === '' ? { blank: true } : null);

/** Publish (notice = null) or edit one notice. A notice goes out as soon as it is saved, to exactly one audience. */
@Component({
  selector: 'app-notice-form-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, InputComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './notice-form-modal.component.scss',
  template: `
    <app-modal [title]="notice() ? 'Edit Notice' : 'New Notice'" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <app-input label="Title" [required]="true" placeholder="e.g. School closed on Monday" formControlName="title" [errorText]="error('title')" />
        <app-textarea label="Notice" [required]="true" placeholder="Write the notice…" [rows]="6" formControlName="body" [errorText]="error('body')" />
        <app-select label="Send to" [required]="true" placeholder="Select audience…" [options]="audienceOptions" formControlName="audience" [errorText]="error('audience')" />
        <button type="submit" hidden tabindex="-1"></button>
      </form>
      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button [disabled]="saving()" (click)="submit()">{{ saving() ? 'Saving…' : notice() ? 'Update' : 'Publish' }}</app-button>
      </ng-container>
    </app-modal>
  `,
})
export class NoticeFormModalComponent implements OnInit {
  private readonly service = inject(NoticeService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  /** The notice being edited, or null to publish a new one. */
  readonly notice = input<Notice | null>(null);
  readonly saved = output<void>();
  readonly closed = output<void>();

  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);
  protected readonly audienceOptions: SelectOption[] = (Object.keys(AUDIENCE_LABELS) as NoticeAudience[]).map((a) => ({ label: AUDIENCE_LABELS[a], value: a }));

  protected readonly form = new FormGroup({
    title: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(MAX_TITLE)] }),
    body: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(MAX_BODY)] }),
    audience: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  ngOnInit(): void {
    const n = this.notice();
    if (n) this.form.patchValue({ title: n.title, body: n.body, audience: n.audience });
  }

  protected error(name: 'title' | 'body' | 'audience'): string | undefined {
    const c = this.form.controls[name];
    if (!(c.touched || this.submitted())) return undefined;
    if (c.hasError('required') || c.hasError('blank')) return 'This field is required';
    if (c.hasError('maxlength')) return `Maximum ${c.getError('maxlength').requiredLength} characters`;
    return undefined;
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    const request = { title: v.title.trim(), body: v.body.trim(), audience: v.audience as NoticeAudience };
    const editing = this.notice();
    this.saving.set(true);
    (editing ? this.service.update(editing.noticeId, request) : this.service.create(request))
      .pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success(`Notice ${editing ? 'updated' : 'published'} successfully.`);
          this.saved.emit();
        },
        error: () => undefined, // already toasted by the interceptor
      });
  }
}
