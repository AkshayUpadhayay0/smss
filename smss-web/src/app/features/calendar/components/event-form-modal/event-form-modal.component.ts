import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, TextareaComponent } from '../../../../shared/components';
import { firstErrorMessage, requiredTrimmed } from '../../../schools/utils/school-validators';
import { EventCategory, SchoolEvent, SchoolEventRequest } from '../../models/calendar.model';
import { SchoolEventService } from '../../services/school-event.service';

function endNotBeforeStart(group: AbstractControl): ValidationErrors | null {
  const start = group.get('startDate')?.value as string;
  const end = group.get('endDate')?.value as string;
  return start && end && end < start ? { endBeforeStart: true } : null;
}

/** Add (event = null) or edit one calendar event. The academic year is worked out by the server from the dates. */
@Component({
  selector: 'app-event-form-modal',
  imports: [ReactiveFormsModule, ButtonComponent, InputComponent, ModalComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './event-form-modal.component.scss',
  templateUrl: './event-form-modal.component.html',
})
export class EventFormModalComponent implements OnInit {
  private readonly service = inject(SchoolEventService);
  private readonly toast = inject(ToastService);

  readonly event = input<SchoolEvent | null>(null);
  /** yyyy-MM-dd pre-filled as the start date when adding. */
  readonly initialDate = input<string>('');
  readonly categories = input<EventCategory[]>([]);

  readonly saved = output<SchoolEvent>();
  readonly closed = output<void>();

  protected readonly editing = computed(() => !!this.event());
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected readonly form = new FormGroup(
    {
      eventTitle: new FormControl('', { nonNullable: true, validators: [requiredTrimmed, Validators.maxLength(200)] }),
      isAllDay: new FormControl(true, { nonNullable: true }),
      startDate: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
      startTime: new FormControl('', { nonNullable: true }),
      endDate: new FormControl('', { nonNullable: true }),
      endTime: new FormControl('', { nonNullable: true }),
      eventCategoryId: new FormControl(0, { nonNullable: true, validators: [Validators.min(1)] }),
      description: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(2000)] }),
    },
    { validators: [endNotBeforeStart] },
  );
  protected readonly allDay = toSignal(this.form.controls.isAllDay.valueChanges, { initialValue: true });
  protected readonly categoryId = toSignal(this.form.controls.eventCategoryId.valueChanges, { initialValue: 0 });

  constructor() {
    this.form.controls.startDate.valueChanges.subscribe((start) => {
      // the end date follows the start while it is blank or would fall before it
      const end = this.form.controls.endDate.value;
      if (start && (!end || end < start)) this.form.controls.endDate.setValue(start, { emitEvent: false });
    });
  }

  ngOnInit(): void {
    const e = this.event();
    if (e) {
      this.form.setValue({
        eventTitle: e.eventTitle, isAllDay: e.isAllDay, startDate: e.startDate, startTime: e.startTime?.slice(0, 5) ?? '', endDate: e.endDate,
        endTime: e.endTime?.slice(0, 5) ?? '', eventCategoryId: e.eventCategoryId, description: e.description ?? '',
      });
    } else {
      this.form.patchValue({ startDate: this.initialDate(), endDate: this.initialDate(), eventCategoryId: this.categories()[0]?.eventCategoryId ?? 0 });
    }
  }

  protected err(name: 'eventTitle' | 'startDate' | 'startTime' | 'endDate' | 'endTime' | 'description'): string | undefined {
    if (name === 'startTime' || name === 'endTime') {
      if (!this.allDay() && !this.form.controls[name].value && this.submitted()) return 'Required for a timed event';
      return undefined;
    }
    if (name === 'endDate' && this.form.hasError('endBeforeStart')) return 'The end date cannot be before the start date';
    return firstErrorMessage(this.form.controls[name], this.submitted());
  }

  protected pick(id: number): void {
    this.form.controls.eventCategoryId.setValue(id);
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    const v = this.form.getRawValue();
    const timesMissing = !v.isAllDay && (!v.startTime || !v.endTime);
    if (this.form.invalid || timesMissing) return;
    const sameDay = !v.endDate || v.endDate === v.startDate;
    if (!v.isAllDay && sameDay && v.endTime <= v.startTime) {
      this.toast.warning('The end time must be after the start time.');
      return;
    }

    const request: SchoolEventRequest = {
      eventTitle: v.eventTitle.trim(), startDate: v.startDate, endDate: v.endDate || v.startDate, isAllDay: v.isAllDay,
      startTime: v.isAllDay ? null : v.startTime, endTime: v.isAllDay ? null : v.endTime, eventCategoryId: v.eventCategoryId, description: v.description.trim() || null,
    };
    const e = this.event();
    this.saving.set(true);
    (e ? this.service.update(e.eventId, request) : this.service.create(request))
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (saved) => {
          this.toast.success(e ? 'Event updated.' : 'Event added.');
          this.saved.emit(saved);
        },
        error: () => undefined, // the server's reason (e.g. the date is outside every academic year) is toasted by the interceptor; the form stays as typed
      });
  }
}
