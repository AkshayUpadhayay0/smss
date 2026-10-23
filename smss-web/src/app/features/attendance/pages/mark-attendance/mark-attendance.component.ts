import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { MarkableSection, PRESENT, SectionAttendance } from '../../models/attendance.model';
import { AttendanceService } from '../../services/attendance.service';

/**
 * The class teacher's daily attendance screen. Everyone starts as Present; tap a student to flip them (Present -> Absent -> ... -> Present),
 * then Submit once for the whole section. If today was already marked, the saved marks are shown and Submit updates them.
 */
@Component({
  selector: 'app-mark-attendance',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './mark-attendance.component.scss',
  templateUrl: './mark-attendance.component.html',
})
export class MarkAttendanceComponent implements OnInit {
  private readonly service = inject(AttendanceService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Attendance' }, { label: 'Mark Attendance' }];
  protected readonly fmt = formatDate;

  protected readonly sections = signal<MarkableSection[]>([]);
  protected readonly today = signal('');
  protected readonly yearName = signal('');
  protected readonly sectionControl = new FormControl('', { nonNullable: true });
  protected readonly sectionOptions = computed<SelectOption[]>(() =>
    this.sections().map((s) => ({ label: `${s.className} – ${s.sectionName}${s.markedToday ? ' (marked)' : ''}`, value: String(s.sectionId) })),
  );

  protected readonly data = signal<SectionAttendance | null>(null);
  /** studentId -> chosen attendanceStatusId (what will be submitted). */
  protected readonly choice = signal<ReadonlyMap<string, number>>(new Map());
  /** The saved state, to know when there is something unsaved. */
  private readonly saved = signal<ReadonlyMap<string, number>>(new Map());

  protected readonly loadingSections = signal(true);
  protected readonly sectionsFailed = signal(false);
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);

  private readonly defaultStatusId = computed(() => (this.data()?.statuses ?? []).find((s) => s.statusCode === PRESENT)?.attendanceStatusId ?? this.data()?.statuses[0]?.attendanceStatusId ?? 0);
  protected readonly students = computed(() => (this.data()?.students ?? []).filter((s) => s.onRoster));
  /** Count per status for the summary line, in the lookup's order. */
  protected readonly counts = computed(() => {
    const picked = this.choice();
    return (this.data()?.statuses ?? []).map((st) => ({ name: st.statusName, code: st.statusCode, n: this.students().filter((s) => picked.get(s.studentId) === st.attendanceStatusId).length }));
  });
  protected readonly dirty = computed(() => {
    const a = this.choice();
    const b = this.saved();
    return this.students().some((s) => a.get(s.studentId) !== b.get(s.studentId));
  });
  protected readonly alreadyMarked = computed(() => !!this.data()?.isMarked);
  protected readonly canSubmit = computed(() => !!this.data()?.canSubmit && this.students().length > 0 && !this.saving());

  ngOnInit(): void {
    this.sectionControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => this.loadSection(v ? Number(v) : null));
    this.loadSections();
  }

  protected loadSections(): void {
    this.loadingSections.set(true);
    this.sectionsFailed.set(false);
    this.service
      .mySections()
      .pipe(finalize(() => this.loadingSections.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (m) => {
          this.sections.set(m.sections);
          this.today.set(m.today);
          this.yearName.set(m.academicYearName);
          // a teacher with one section lands straight in it
          if (m.sections.length >= 1) this.sectionControl.setValue(String(m.sections[0].sectionId));
        },
        error: () => this.sectionsFailed.set(true), // already toasted by the interceptor
      });
  }

  private loadSection(sectionId: number | null): void {
    this.data.set(null);
    this.loadFailed.set(false);
    if (!sectionId) return;
    this.loading.set(true);
    this.service
      .section(sectionId)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) => this.apply(d),
        error: () => this.loadFailed.set(true),
      });
  }

  private apply(d: SectionAttendance): void {
    this.data.set(d);
    const def = d.statuses.find((s) => s.statusCode === PRESENT)?.attendanceStatusId ?? d.statuses[0]?.attendanceStatusId ?? 0;
    const saved = new Map<string, number>();
    const picked = new Map<string, number>();
    for (const s of d.students.filter((x) => x.onRoster)) {
      if (s.attendanceStatusId != null) saved.set(s.studentId, s.attendanceStatusId);
      picked.set(s.studentId, s.attendanceStatusId ?? def);   // unmarked students start as Present
    }
    this.saved.set(saved);
    this.choice.set(picked);
  }

  protected statusOf(studentId: string) {
    const id = this.choice().get(studentId);
    return this.data()?.statuses.find((s) => s.attendanceStatusId === id);
  }

  /** One tap: the next status in the lookup's order (Present -> Absent -> Present for now). */
  protected flip(studentId: string): void {
    const statuses = this.data()?.statuses ?? [];
    if (!this.data()?.canSubmit || statuses.length === 0 || this.saving()) return;
    const at = statuses.findIndex((s) => s.attendanceStatusId === this.choice().get(studentId));
    const next = statuses[(at + 1) % statuses.length].attendanceStatusId;
    this.choice.update((m) => new Map(m).set(studentId, next));
  }

  protected markAllPresent(): void {
    if (!this.data()?.canSubmit || this.saving()) return;
    const def = this.defaultStatusId();
    this.choice.set(new Map(this.students().map((s) => [s.studentId, def])));
  }

  protected submit(): void {
    const d = this.data();
    if (!d || !this.canSubmit()) return;
    this.saving.set(true);
    this.service
      .submit({ sectionId: d.sectionId, date: d.date, records: this.students().map((s) => ({ studentId: s.studentId, attendanceStatusId: this.choice().get(s.studentId)! })) })
      .pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.toast.success(`Attendance saved for ${d.className} – ${d.sectionName}: ${r.present} present, ${r.absent} absent.`);
          this.sections.update((l) => l.map((s) => (s.sectionId === d.sectionId ? { ...s, markedToday: true } : s)));
          this.loadSection(d.sectionId); // the saved state, with who marked and when
        },
        error: () => undefined, // the server's reason (missing students, not the class teacher, ...) is toasted by the interceptor; the choices stay
      });
  }
}
