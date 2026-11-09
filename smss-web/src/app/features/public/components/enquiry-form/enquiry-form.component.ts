import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { InputComponent } from '../../../../shared/components/input/input.component';
import { SelectComponent, SelectOption } from '../../../../shared/components/select/select.component';
import { TextareaComponent } from '../../../../shared/components/textarea/textarea.component';
import {
  CreateEnquiryRequest, EMAIL_PATTERN, EnquiryType, PHONE_PATTERN, STUDENT_COUNT_RANGES, TIME_SLOTS,
} from '../../models/enquiry.model';
import { PublicEnquiryService } from '../../services/public-enquiry.service';

const toOption = (v: string): SelectOption => ({ label: v, value: v });

/** yyyy-MM-dd of today in the visitor's own calendar. */
function todayIso(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function notInPast(control: AbstractControl): ValidationErrors | null {
  const v = control.value as string;
  return v && v < todayIso() ? { past: true } : null;
}

/**
 * The one form behind both public pages: type "CONTACT" (Contact Us) or "DEMO" (Request a Demo).
 * Validation mirrors EnquiryRules on the API; the API answers 400 with field messages if anything slips through.
 */
@Component({
  selector: 'app-enquiry-form',
  imports: [ReactiveFormsModule, RouterLink, IconComponent, InputComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './enquiry-form.component.scss',
  template: `
    @if (done()) {
      <div class="success" role="status">
        <span class="tick"><app-icon name="circle-check" [size]="30" /></span>
        <h2>{{ isDemo() ? 'Demo request received' : 'Message sent' }}</h2>
        <p>
          {{
            isDemo()
              ? 'Thank you. Our team will contact you shortly to confirm a time for your demo.'
              : 'Thank you for writing to us. We will reply to your email as soon as we can.'
          }}
        </p>
        <a class="pub-btn pub-btn-primary" routerLink="/">Back to home</a>
      </div>
    } @else {
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        @if (formError(); as message) {
          <p class="alert" role="alert"><app-icon name="circle-alert" [size]="18" />{{ message }}</p>
        }

        <div class="grid">
          <app-input label="Full name" [required]="true" formControlName="fullName"[errorText]="fieldError('fullName')" />
          @if (isDemo()) {
            <app-input label="Designation" formControlName="designation" placeholder="e.g. Principal" [errorText]="fieldError('designation')" />
            <app-input label="School name" [required]="true" formControlName="schoolName" [errorText]="fieldError('schoolName')" />
          }
          <app-input label="Email" type="email" [required]="true" formControlName="email" [errorText]="fieldError('email')" />
          <app-input label="Phone" type="tel" [required]="isDemo()" formControlName="phone" placeholder="+91 98765 43210" [errorText]="fieldError('phone')" />

          @if (isDemo()) {
            <app-input label="City" formControlName="city" [errorText]="fieldError('city')" />
            <app-select
              label="Approximate number of students"
              formControlName="studentCountRange"
              placeholder="Select a range"
              [options]="studentRanges"
              [errorText]="fieldError('studentCountRange')"
            />
            <app-input label="Preferred demo date" type="date" formControlName="preferredDemoDate" [errorText]="fieldError('preferredDemoDate')" />
            <app-select
              label="Preferred time slot"
              formControlName="preferredTimeSlot"
              placeholder="Select a time slot"
              [options]="timeSlots"
              [errorText]="fieldError('preferredTimeSlot')"
            />
          } @else {
            <app-input label="Subject" formControlName="subject" class="wide" [errorText]="fieldError('subject')" />
          }
        </div>

        <app-textarea
          [label]="isDemo() ? 'Message (optional)' : 'Message'"
          [required]="!isDemo()"
          [rows]="5"
          [maxLength]="2000"
          formControlName="message"
          [errorText]="fieldError('message')"
        />

        <!-- Honeypot: invisible to people and assistive tech, bots fill it in -->
        <div class="trap" aria-hidden="true">
          <label for="enquiry-website">Leave this field empty</label>
          <input id="enquiry-website" type="text" formControlName="website" tabindex="-1" autocomplete="off" />
        </div>

        <div class="consent">
          <label>
            <input type="checkbox" formControlName="consentGiven" [attr.aria-invalid]="!!fieldError('consentGiven')" />
            <span>
              I agree to the <a class="pub-link" routerLink="/privacy-policy" target="_blank">Privacy Policy</a> and to being contacted about my request.
              <span class="req" aria-hidden="true">*</span>
            </span>
          </label>
          @if (fieldError('consentGiven'); as message) {
            <span class="error">{{ message }}</span>
          }
        </div>

        <button type="submit" class="pub-btn pub-btn-primary submit" [disabled]="submitting()">
          @if (submitting()) {
            <app-icon name="loader-circle" [size]="18" /> Sending…
          } @else {
            <app-icon name="send" [size]="18" /> {{ isDemo() ? 'Request a Demo' : 'Send message' }}
          }
        </button>
      </form>
    }
  `,
})
export class EnquiryFormComponent implements OnInit {
  private readonly service = inject(PublicEnquiryService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);

  readonly type = input.required<EnquiryType>();
  protected readonly isDemo = computed(() => this.type() === 'DEMO');

  protected readonly studentRanges = STUDENT_COUNT_RANGES.map(toOption);
  protected readonly timeSlots = TIME_SLOTS.map(toOption);

  protected readonly submitting = signal(false);
  protected readonly done = signal(false);
  protected readonly formError = signal<string | null>(null);
  private readonly serverErrors = signal<Record<string, string>>({});
  private readonly submitted = signal(false);

  protected readonly form = new FormGroup({
    fullName: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(150)] }),
    designation: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
    schoolName: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(200)] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(200), Validators.pattern(EMAIL_PATTERN)] }),
    phone: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(30), Validators.pattern(PHONE_PATTERN)] }),
    city: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
    studentCountRange: new FormControl('', { nonNullable: true }),
    subject: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(200)] }),
    message: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(2000)] }),
    preferredDemoDate: new FormControl('', { nonNullable: true, validators: [notInPast] }),
    preferredTimeSlot: new FormControl('', { nonNullable: true }),
    consentGiven: new FormControl(false, { nonNullable: true, validators: [Validators.requiredTrue] }),
    website: new FormControl('', { nonNullable: true }),
  });

  ngOnInit(): void {
    const c = this.form.controls;
    if (this.isDemo()) {
      c.schoolName.addValidators(Validators.required);
      c.phone.addValidators(Validators.required);
    } else {
      c.message.addValidators(Validators.required);
    }
    (Object.values(c) as AbstractControl[]).forEach((control) => control.updateValueAndValidity({ emitEvent: false }));

    // Editing a field clears the server's complaint about it
    for (const [name, control] of Object.entries(c) as [string, AbstractControl][]) {
      control.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
        if (this.serverErrors()[name]) this.serverErrors.update(({ [name]: _, ...rest }) => rest);
      });
    }
  }

  protected fieldError(name: keyof typeof this.form.controls): string | undefined {
    const server = this.serverErrors()[name];
    if (server) return server;
    const control = this.form.controls[name];
    if (!control.invalid || !(control.touched || this.submitted())) return undefined;
    const e = control.errors ?? {};
    if (e['required'] || e['requiredTrue']) return name === 'consentGiven' ? 'Please accept the privacy policy to continue.' : 'This field is required.';
    if (e['pattern']) return name === 'email' ? 'Enter a valid email address.' : 'Enter a valid phone number.';
    if (e['maxlength']) return `Please keep this to ${e['maxlength'].requiredLength} characters or fewer.`;
    if (e['past']) return 'Choose today or a later date.';
    return 'Please check this field.';
  }

  protected submit(): void {
    if (this.submitting()) return;
    this.submitted.set(true);
    this.formError.set(null);
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.focusFirstInvalid();
      return;
    }

    this.submitting.set(true);
    this.service
      .submit(this.toRequest())
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: () => this.done.set(true),
        error: (error: unknown) => {
          const failure = this.service.toFailure(error);
          this.serverErrors.set(failure.fields);
          this.formError.set(failure.message);
        },
      });
  }

  private toRequest(): CreateEnquiryRequest {
    const v = this.form.getRawValue();
    const demo = this.isDemo();
    const blank = (s: string) => (s.trim() ? s.trim() : null);
    return {
      enquiryType: this.type(),
      fullName: v.fullName.trim(),
      email: v.email.trim(),
      phone: blank(v.phone),
      message: blank(v.message),
      designation: demo ? blank(v.designation) : null,
      schoolName: demo ? blank(v.schoolName) : null,
      city: demo ? blank(v.city) : null,
      studentCountRange: demo ? blank(v.studentCountRange) : null,
      preferredDemoDate: demo ? blank(v.preferredDemoDate) : null,
      preferredTimeSlot: demo ? blank(v.preferredTimeSlot) : null,
      subject: demo ? null : blank(v.subject),
      consentGiven: v.consentGiven,
      website: v.website,
    };
  }

  private focusFirstInvalid(): void {
    // Runs after the error texts have rendered
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus(), 0);
  }
}
