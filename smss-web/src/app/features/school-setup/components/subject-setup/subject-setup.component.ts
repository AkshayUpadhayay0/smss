import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { SaveYearClassSubjectsRequest, SubjectDraft, YearClassSubjects } from '../../models/academic-setup.model';
import { AcademicSetupService } from '../../services/academic-setup.service';

/** The server's answer as an editable draft. */
export function toSubjectDraft(d: YearClassSubjects): SubjectDraft {
  return {
    mapped: new Set(d.subjects.filter((s) => s.mapped).map((s) => s.subjectId)),
    sections: d.sections.map((s) => ({ sectionId: s.sectionId, sectionName: s.sectionName, extra: new Set(s.extraSubjectIds), notTaught: new Set(s.notTaughtSubjectIds) })),
  };
}

/** The request the screen sends: the whole class set, and only the sections that have an exception. An exception that the class set makes meaningless is dropped. */
export function toSubjectRequest(draft: SubjectDraft): SaveYearClassSubjectsRequest {
  return {
    subjectIds: [...draft.mapped].sort((a, b) => a - b),
    sections: draft.sections
      .map((s) => ({
        sectionId: s.sectionId,
        extraSubjectIds: [...s.extra].filter((id) => !draft.mapped.has(id)).sort((a, b) => a - b),
        notTaughtSubjectIds: [...s.notTaught].filter((id) => draft.mapped.has(id)).sort((a, b) => a - b),
      }))
      .filter((s) => s.extraSubjectIds.length + s.notTaughtSubjectIds.length > 0),
  };
}

/** What a section is taught = the class set - "not taught here" + "also taught here". */
export function effectiveCount(draft: SubjectDraft, sectionId: number): number {
  const s = draft.sections.find((x) => x.sectionId === sectionId);
  if (!s) return draft.mapped.size;
  return [...draft.mapped].filter((id) => !s.notTaught.has(id)).length + [...s.extra].filter((id) => !draft.mapped.has(id)).length;
}

/**
 * Academic Setup, Subjects (S3): which subjects ONE class is taught in the chosen academic year, and per section the few exceptions
 * ("also taught here" / "not taught here"). Save sends the whole set; a closed year is read-only; a subject something in the year uses can't be dropped.
 */
@Component({
  selector: 'app-subject-setup',
  imports: [ReactiveFormsModule, ButtonComponent, IconComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './subject-setup.component.scss',
  templateUrl: './subject-setup.component.html',
})
export class SubjectSetupComponent {
  private readonly setup = inject(AcademicSetupService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly yearId = input.required<number>();
  /** The classes the year offers (from the Classes tab). */
  readonly classes = input.required<{ classId: number; className: string }[]>();
  readonly readOnly = input(false);

  protected readonly classControl = new FormControl('', { nonNullable: true });
  protected readonly classOptions = computed<SelectOption[]>(() => this.classes().map((c) => ({ label: c.className, value: String(c.classId) })));

  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly data = signal<YearClassSubjects | null>(null);
  protected readonly draft = signal<SubjectDraft>({ mapped: new Set(), sections: [] });
  private readonly savedKey = signal('');

  protected readonly locked = computed(() => this.readOnly() || this.data()?.isClosed === true);
  protected readonly dirty = computed(() => this.key(this.draft()) !== this.savedKey());
  protected readonly subjects = computed(() => this.data()?.subjects ?? []);
  protected readonly noSubjectsAtAll = computed(() => !this.loading() && this.data() != null && this.subjects().length === 0);

  constructor() {
    this.classControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id) => { if (id) this.load(Number(id)); });
    // a different year (or a changed class list) keeps the class when it is still offered, otherwise starts on the first one
    effect(() => {
      this.yearId();
      const list = this.classes();
      untracked(() => {
        const keep = list.some((c) => String(c.classId) === this.classControl.value);
        if (keep) this.load(Number(this.classControl.value));
        else if (list.length) this.classControl.setValue(String(list[0].classId));
        else { this.data.set(null); this.classControl.setValue('', { emitEvent: false }); }
      });
    });
  }

  protected load(classId: number): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.setup.getClassSubjects(this.yearId(), classId).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (d) => this.apply(d),
      error: () => { this.data.set(null); this.loadFailed.set(true); },
    });
  }

  private apply(d: YearClassSubjects): void {
    this.data.set(d);
    const draft = toSubjectDraft(d);
    this.draft.set(draft);
    this.savedKey.set(this.key(draft));
  }

  private key(d: SubjectDraft): string {
    return JSON.stringify([[...d.mapped].sort((a, b) => a - b), d.sections.map((s) => [s.sectionId, [...s.extra].sort((a, b) => a - b), [...s.notTaught].sort((a, b) => a - b)])]);
  }

  /** The subject is in use somewhere in this class this year (so the class can't drop it). */
  protected inUse(subjectId: number): boolean {
    return (this.data()?.subjects.find((s) => s.subjectId === subjectId)?.inUse ?? 0) > 0;
  }

  protected inUseInSection(sectionId: number, subjectId: number): boolean {
    return this.data()?.sections.find((s) => s.sectionId === sectionId)?.inUseSubjectIds.includes(subjectId) ?? false;
  }

  protected sectionCount(sectionId: number): number {
    return effectiveCount(this.draft(), sectionId);
  }

  // ---------------- editing ----------------
  protected toggleClassSubject(subjectId: number, on: boolean): void {
    this.draft.update((d) => {
      const mapped = new Set(d.mapped);
      if (on) mapped.add(subjectId); else mapped.delete(subjectId);
      // an exception that no longer makes sense is dropped (a subject the class now has can't be "extra"; one it lacks can't be "not taught")
      return { mapped, sections: d.sections.map((s) => ({ ...s, extra: new Set([...s.extra].filter((x) => !mapped.has(x))), notTaught: new Set([...s.notTaught].filter((x) => mapped.has(x))) })) };
    });
  }

  protected toggleException(sectionId: number, subjectId: number, kind: 'extra' | 'notTaught', on: boolean): void {
    this.draft.update((d) => ({
      ...d,
      sections: d.sections.map((s) => {
        if (s.sectionId !== sectionId) return s;
        const set = new Set(s[kind]);
        if (on) set.add(subjectId); else set.delete(subjectId);
        return { ...s, [kind]: set };
      }),
    }));
  }

  protected isExtra(sectionId: number, subjectId: number): boolean {
    return this.draft().sections.find((s) => s.sectionId === sectionId)?.extra.has(subjectId) ?? false;
  }

  protected isNotTaught(sectionId: number, subjectId: number): boolean {
    return this.draft().sections.find((s) => s.sectionId === sectionId)?.notTaught.has(subjectId) ?? false;
  }

  protected reset(): void {
    const d = this.data();
    if (d) this.apply(d);
  }

  protected save(): void {
    const d = this.data();
    if (!d) return;
    this.saving.set(true);
    this.setup.saveClassSubjects(d.academicYearId, d.classId, toSubjectRequest(this.draft())).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (saved) => {
        this.apply(saved);
        this.toast.success(`Saved the subjects of ${saved.className} for ${saved.yearName}.`);
      },
      error: () => undefined,   // the server's reason ("can't be removed while in use") is already toasted by the interceptor
    });
  }
}
