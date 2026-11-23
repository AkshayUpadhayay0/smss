import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent, ModalComponent } from '../../../../shared/components';
import { ApplyResult, TimetableSummary, TimetableValidation } from '../../models/timetable.model';
import { TimetableService } from '../../services/timetable.service';

export const DAY_NAMES = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/** "ACTIVE" -> "Active" for the badge. */
export const stateLabel = (s: TimetableSummary['state']): string => ({ ACTIVE: 'Active', DRAFT: 'Draft', ARCHIVED: 'Archived' })[s];

/** "2026-11-23T10:15:00Z" -> "23 Nov 2026" (the calendar day of the instant, as a reader sees it). */
export function whenLabel(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
}

/** What a row's buttons may do (the server enforces all of it again). */
export function actionsOf(t: TimetableSummary, readOnly: boolean): { activate: boolean; archive: boolean; restore: boolean; remove: boolean; edit: boolean } {
  if (readOnly) return { activate: false, archive: false, restore: false, remove: false, edit: false };
  return {
    activate: !t.isActive,
    archive: t.state === 'DRAFT',
    restore: t.state === 'ARCHIVED' && !t.everActive,
    remove: t.canDelete && !t.isActive,
    edit: t.state !== 'ARCHIVED',
  };
}

type Dialog = 'create' | 'rename' | 'check' | 'apply';

/**
 * The timetables (versions) of ONE section in ONE year: exactly one is Active; drafts are worked on and activated; an old active one is archived, never lost.
 * Create (blank or copy), rename, check (a dry run that saves nothing), activate, archive / restore, delete a draft that never went live, and
 * "Apply to other sections" (a preview first, then independent drafts).
 */
@Component({
  selector: 'app-timetable-versions',
  imports: [ButtonComponent, IconComponent, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './timetable-versions.component.scss',
  templateUrl: './timetable-versions.component.html',
})
export class TimetableVersionsComponent {
  private readonly api = inject(TimetableService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmDialogService);

  readonly sectionId = input.required<number>();
  readonly yearId = input.required<number>();
  readonly versions = input.required<TimetableSummary[]>();
  readonly selectedId = input<number | null>(null);
  readonly readOnly = input(false);
  /** The other sections of the same class that the year offers (the targets of "Apply to other sections"). */
  readonly siblings = input<{ sectionId: number; sectionName: string }[]>([]);

  /** A row was opened in the grid editor. */
  readonly picked = output<number>();
  /** The list changed on the server: reload it (and show this timetable, when given). */
  readonly changed = output<number | null>();

  protected readonly busy = signal(false);
  protected readonly dialog = signal<Dialog | null>(null);
  protected readonly target = signal<TimetableSummary | null>(null);

  // create / rename
  protected readonly name = signal('');
  protected readonly cloneFrom = signal('');
  // check
  protected readonly validation = signal<TimetableValidation | null>(null);
  protected readonly checking = signal(false);
  // apply
  protected readonly picks = signal<number[]>([]);
  protected readonly applyName = signal('');
  protected readonly preview = signal<ApplyResult | null>(null);

  protected readonly label = stateLabel;
  protected readonly when = whenLabel;
  protected readonly days = DAY_NAMES;
  protected readonly can = (t: TimetableSummary) => actionsOf(t, this.readOnly());
  protected readonly hasActive = computed(() => this.versions().some((v) => v.isActive));
  protected readonly canApply = computed(() => !this.readOnly() && this.siblings().length > 0 && this.versions().length > 0);

  // ---------------- dialogs
  protected openCreate(from: TimetableSummary | null): void {
    this.dialog.set('create');
    this.target.set(from);
    this.cloneFrom.set(from ? String(from.timetableId) : '');
    this.name.set(from ? `${from.timetableName} copy` : this.versions().length === 0 ? 'Regular timetable' : '');
  }

  protected openRename(t: TimetableSummary): void {
    this.dialog.set('rename');
    this.target.set(t);
    this.name.set(t.timetableName);
  }

  protected openCheck(t: TimetableSummary): void {
    this.dialog.set('check');
    this.target.set(t);
    this.validation.set(null);
    this.checking.set(true);
    this.api.validateVersion(t.timetableId).pipe(finalize(() => this.checking.set(false))).subscribe({ next: (v) => this.validation.set(v), error: () => undefined });
  }

  protected openApply(t: TimetableSummary): void {
    this.dialog.set('apply');
    this.target.set(t);
    this.picks.set([]);
    this.applyName.set(t.timetableName);
    this.preview.set(null);
  }

  protected close(): void {
    this.dialog.set(null);
    this.target.set(null);
    this.preview.set(null);
  }

  protected togglePick(id: number, on: boolean): void {
    this.picks.update((l) => (on ? [...l.filter((x) => x !== id), id] : l.filter((x) => x !== id)));
    this.preview.set(null);   // what was previewed is no longer what would be created
  }

  // ---------------- actions
  protected save(): void {
    const name = this.name().trim();
    if (!name || this.busy()) return;
    this.busy.set(true);
    const call = this.dialog() === 'rename'
      ? this.api.renameVersion(this.target()!.timetableId, name)
      : this.api.createVersion(this.sectionId(), this.yearId(), name, this.cloneFrom() ? Number(this.cloneFrom()) : null);
    call.pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (t) => { this.toast.success(this.dialog() === 'rename' ? 'Timetable renamed.' : `Timetable "${t.timetableName}" created as a draft.`); const id = this.dialog() === 'create' ? t.timetableId : null; this.close(); this.changed.emit(id); },
      error: () => undefined,   // the server's reason (name taken, closed year) is already toasted
    });
  }

  protected async activate(t: TimetableSummary): Promise<void> {
    const active = this.versions().find((v) => v.isActive);
    const lines = [active ? `"${active.timetableName}" stops being the active timetable and is kept as history.` : `"${t.timetableName}" becomes the timetable everyone sees.`];
    if (t.needsTeacherCount > 0) lines.push(`${t.needsTeacherCount} slot${t.needsTeacherCount === 1 ? ' has' : 's have'} no teacher yet.`);
    const ok = await this.confirm.confirm({ title: `Activate "${t.timetableName}"?`, message: lines.join(' '), confirmText: 'Activate' });
    if (!ok) return;
    this.busy.set(true);
    this.api.activateVersion(t.timetableId).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (a) => { this.toast.success(a.slotsWithoutTeacher > 0 ? `Activated. ${a.slotsWithoutTeacher} slot(s) still need a teacher.` : 'Timetable activated.'); this.changed.emit(t.timetableId); },
      error: () => this.openCheck(t),   // it could not be activated: show every problem, not only the first
    });
  }

  protected archive(t: TimetableSummary): void {
    this.api.archiveVersion(t.timetableId).subscribe({ next: () => { this.toast.success('Timetable archived.'); this.changed.emit(null); }, error: () => undefined });
  }

  protected restore(t: TimetableSummary): void {
    this.api.restoreVersion(t.timetableId).subscribe({ next: () => { this.toast.success('Timetable restored as a draft.'); this.changed.emit(t.timetableId); }, error: () => undefined });
  }

  protected async remove(t: TimetableSummary): Promise<void> {
    const ok = await this.confirm.confirm({ title: 'Delete draft', message: `Delete the draft "${t.timetableName}" and its ${t.slotCount} slots? A timetable that was ever active can't be deleted.`, confirmText: 'Delete', variant: 'danger' });
    if (!ok) return;
    this.api.deleteVersion(t.timetableId).subscribe({ next: () => { this.toast.success('Draft deleted.'); this.changed.emit(null); }, error: () => undefined });
  }

  protected runPreview(): void {
    const t = this.target();
    if (!t || this.picks().length === 0 || this.busy()) return;
    this.busy.set(true);
    this.api.previewApply(t.timetableId, this.picks(), this.applyName().trim() || null).pipe(finalize(() => this.busy.set(false))).subscribe({ next: (r) => this.preview.set(r), error: () => undefined });
  }

  protected runApply(): void {
    const t = this.target();
    if (!t || !this.preview() || this.busy()) return;
    this.busy.set(true);
    this.api.apply(t.timetableId, this.picks(), this.applyName().trim() || null).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (r) => { const n = r.sections.filter((s) => s.created).length; this.toast.success(`${n} draft timetable${n === 1 ? '' : 's'} created.`); this.close(); this.changed.emit(null); },
      error: () => undefined,
    });
  }

  protected readonly createdCount = computed(() => this.preview()?.sections.filter((s) => s.created).length ?? 0);
}
