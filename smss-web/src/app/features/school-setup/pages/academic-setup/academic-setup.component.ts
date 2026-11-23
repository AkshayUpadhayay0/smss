import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { AcademicYear } from '../../models/academic-year.model';
import { ClassDraft, SaveYearOfferingRequest, SectionDraft, YearOffering } from '../../models/academic-setup.model';
import { AcademicSetupService } from '../../services/academic-setup.service';
import { AcademicYearService } from '../../services/academic-year.service';
import { ScheduleAssignmentsComponent } from '../../components/schedule-assignments/schedule-assignments.component';
import { ScheduleManagerComponent } from '../../components/schedule-manager/schedule-manager.component';
import { SubjectSetupComponent } from '../../components/subject-setup/subject-setup.component';

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

/** Capacity text -> number; '' = use the section master's capacity (null); anything that is not a whole number 1..32767 = NaN. */
export function parseCapacity(text: string): number | null {
  const t = text.trim();
  if (t === '') return null;
  const n = Number(t);
  return Number.isInteger(n) && n >= 1 && n <= 32767 ? n : Number.NaN;
}

/** The server's answer as editable drafts, with the reason a class or section can't be switched off while the year uses it. */
export function toDrafts(o: YearOffering): ClassDraft[] {
  return o.classes.map((c) => {
    const sections: SectionDraft[] = c.sections.map((s) => {
      const why: string[] = [];
      if (s.enrolments > 0) why.push(`${plural(s.enrolments, 'student is', 'students are')} enrolled`);
      if (s.hasClassTeacher) why.push('it has a class teacher');
      if (s.timetableEntries > 0) why.push(`${plural(s.timetableEntries, 'timetable slot', 'timetable slots')}`);
      return {
        sectionId: s.sectionId, sectionName: s.sectionName, isActive: s.isActive, offered: s.offered,
        capacity: s.maxStrength != null ? String(s.maxStrength) : '', masterCapacity: s.masterMaxStrength ?? null,
        lockedReason: s.offered && why.length ? why.join(', ') : null,
      };
    });
    const classWhy: string[] = [];
    if (c.hasFeeStructure) classWhy.push('it has a fee structure for this year');
    const lockedSection = sections.find((s) => s.lockedReason);
    if (lockedSection) classWhy.push(`section ${lockedSection.sectionName} is in use`);
    return { classId: c.classId, className: c.className, isActive: c.isActive, offered: c.offered, lockedReason: c.offered && classWhy.length ? classWhy.join(', ') : null, sections };
  });
}

/** The request the screen sends: only what is offered. */
export function toRequest(drafts: ClassDraft[]): SaveYearOfferingRequest {
  return {
    classes: drafts.filter((c) => c.offered).map((c) => ({
      classId: c.classId,
      sections: c.sections.filter((s) => s.offered).map((s) => ({ sectionId: s.sectionId, maxStrength: parseCapacity(s.capacity) })),
    })),
  };
}

/**
 * Academic Setup (S2): which classes and sections the school offers in ONE academic year, and each section's capacity for that year.
 * The class and section masters themselves stay under School Setup. Pick a year, tick, Save: the whole offered set is sent and the server replaces it
 * (a closed year is read-only; something already in use can't be switched off).
 */
@Component({
  selector: 'app-academic-setup',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, ScheduleAssignmentsComponent, ScheduleManagerComponent, SelectComponent, SubjectSetupComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './academic-setup.component.scss',
  templateUrl: './academic-setup.component.html',
})
export class AcademicSetupComponent implements OnInit {
  private readonly years = inject(AcademicYearService);
  private readonly setup = inject(AcademicSetupService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'School Setup' }, { label: 'Academic Setup' }];
  protected readonly yearControl = new FormControl('', { nonNullable: true });
  protected readonly yearOptions = signal<SelectOption[]>([]);
  private readonly yearList = signal<AcademicYear[]>([]);

  protected readonly loadingYears = signal(true);
  protected readonly yearsFailed = signal(false);
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);

  protected readonly tab = signal<'classes' | 'subjects' | 'schedules'>('classes');
  protected readonly offering = signal<YearOffering | null>(null);
  /** The classes the year offers, as last SAVED (the subjects tab works on these). */
  protected readonly offeredClassList = computed(() => (this.offering()?.classes ?? []).filter((c) => c.offered).map((c) => ({ classId: c.classId, className: c.className })));
  protected readonly draft = signal<ClassDraft[]>([]);
  private readonly savedKey = signal('');

  protected readonly readOnly = computed(() => this.offering()?.isClosed === true);
  protected readonly offeredClasses = computed(() => this.draft().filter((c) => c.offered).length);
  protected readonly offeredSections = computed(() => this.draft().filter((c) => c.offered).reduce((n, c) => n + c.sections.filter((s) => s.offered).length, 0));
  protected readonly dirty = computed(() => this.key(this.draft()) !== this.savedKey());
  protected readonly hasBadCapacity = computed(() => this.draft().some((c) => c.offered && c.sections.some((s) => s.offered && Number.isNaN(parseCapacity(s.capacity)))));
  protected readonly nothingToOffer = computed(() => !this.loading() && this.offering() != null && this.draft().length === 0);

  ngOnInit(): void {
    this.yearControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id) => {
      if (id) this.loadOffering(Number(id));
    });
    this.loadYears();
  }

  protected loadYears(): void {
    this.loadingYears.set(true);
    this.yearsFailed.set(false);
    this.years.list().pipe(finalize(() => this.loadingYears.set(false))).subscribe({
      next: (list) => {
        this.yearList.set(list);
        this.yearOptions.set(list.map((y) => ({ label: this.yearLabel(y), value: String(y.academicYearId) })));
        const first = list.find((y) => y.isCurrent) ?? list[0];
        if (first) this.yearControl.setValue(String(first.academicYearId));
      },
      error: () => this.yearsFailed.set(true),   // already toasted by the interceptor
    });
  }

  protected loadOffering(yearId: number): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.setup.getOffering(yearId).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (o) => this.apply(o),
      error: () => { this.offering.set(null); this.draft.set([]); this.loadFailed.set(true); },
    });
  }

  private apply(o: YearOffering): void {
    this.offering.set(o);
    const drafts = toDrafts(o);
    this.draft.set(drafts);
    this.savedKey.set(this.key(drafts));
  }

  private yearLabel(y: AcademicYear): string {
    return `${y.yearName}${y.isCurrent ? ' (current)' : ''}${y.isClosed ? ' (closed)' : ''}`;
  }

  /** What makes two drafts "the same": what is offered and the capacities of the offered sections. */
  private key(drafts: ClassDraft[]): string {
    return JSON.stringify(drafts.map((c) => [c.classId, c.offered, c.offered ? c.sections.filter((s) => s.offered).map((s) => [s.sectionId, s.capacity.trim()]) : []]));
  }

  // ---------------- editing ----------------
  protected toggleClass(classId: number, on: boolean): void {
    this.draft.update((list) => list.map((c) => {
      if (c.classId !== classId) return c;
      if (!on && c.lockedReason) return c;
      // switching a class on offers all its active sections (the usual wish); off withdraws them all
      return { ...c, offered: on, sections: c.sections.map((s) => ({ ...s, offered: on ? s.isActive : false })) };
    }));
  }

  protected toggleSection(classId: number, sectionId: number, on: boolean): void {
    this.draft.update((list) => list.map((c) => {
      if (c.classId !== classId) return c;
      return { ...c, offered: on ? true : c.offered, sections: c.sections.map((s) => (s.sectionId === sectionId && !(!on && s.lockedReason) ? { ...s, offered: on } : s)) };
    }));
  }

  protected setCapacity(classId: number, sectionId: number, text: string): void {
    this.draft.update((list) => list.map((c) => (c.classId !== classId ? c : { ...c, sections: c.sections.map((s) => (s.sectionId === sectionId ? { ...s, capacity: text } : s)) })));
  }

  protected capacityInvalid(s: SectionDraft): boolean {
    return s.offered && Number.isNaN(parseCapacity(s.capacity));
  }

  /** Every active class with all its active sections. */
  protected offerAll(): void {
    this.draft.update((list) => list.map((c) => (c.isActive ? { ...c, offered: true, sections: c.sections.map((s) => ({ ...s, offered: s.isActive })) } : c)));
  }

  /** Switches off everything that is not in use. */
  protected clearUnused(): void {
    this.draft.update((list) => list.map((c) => {
      const sections = c.sections.map((s) => (s.lockedReason ? s : { ...s, offered: false }));
      return c.lockedReason || sections.some((s) => s.offered) ? { ...c, sections } : { ...c, offered: false, sections };
    }));
  }

  protected reset(): void {
    const o = this.offering();
    if (o) this.apply(o);
  }

  protected save(): void {
    const o = this.offering();
    if (!o || this.hasBadCapacity()) return;
    this.saving.set(true);
    this.setup.saveOffering(o.academicYearId, toRequest(this.draft())).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (saved) => {
        this.apply(saved);
        this.toast.success(`Saved: ${saved.offeredClasses} classes and ${saved.offeredSections} sections in ${saved.yearName}.`);
      },
      error: () => undefined,   // the server's reason (e.g. "can't be removed while in use") is already toasted by the interceptor
    });
  }
}
