import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { finalize, forkJoin } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, PageHeaderComponent } from '../../../../shared/components';
import { AcademicYear } from '../../../school-setup/models/academic-year.model';
import { SchoolClass } from '../../../school-setup/models/class.model';
import { AcademicYearService } from '../../../school-setup/services/academic-year.service';
import { ClassService } from '../../../school-setup/services/class.service';
import { ReportTemplate, SchemePreset, SchemeFromPresetRequest, splitYear } from '../../models/examinations.model';
import { ExaminationsService } from '../../services/examinations.service';

/**
 * The three-step way to a working scheme: 1. pick a preset (and the year), 2. pick the classes, 3. check the term dates and rules, then create.
 * The server builds everything else (grade scale, terms, exams, the class's mapped subjects, the whole matrix, skills) in one call.
 */
@Component({
  selector: 'app-scheme-from-preset',
  imports: [ButtonComponent, CardComponent, PageHeaderComponent, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    <app-page-header title="New Report Scheme" subtitle="Three steps: preset, classes, review" icon="file-text" [breadcrumbs]="breadcrumbs" />

    @if (loading()) {
      <app-card><p class="note" role="status">Loading…</p></app-card>
    } @else if (failed()) {
      <app-card><div class="note"><p role="alert">The set-up could not be loaded.</p><app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></div></app-card>
    } @else {
      <ol class="steps" aria-label="Steps">
        @for (s of stepNames; track s; let i = $index) {
          <li [class.on]="step() === i + 1" [class.done]="step() > i + 1" [attr.aria-current]="step() === i + 1 ? 'step' : null"><span>{{ i + 1 }}</span>{{ s }}</li>
        }
      </ol>

      @switch (step()) {
        @case (1) {
          <app-card title="1. What kind of report card?">
            <div class="grid-2 top">
              <label class="field"><span>Academic year</span>
                <select [value]="yearId()" (change)="yearId.set($any($event.target).value)" aria-label="Academic year">
                  @for (y of years(); track y.academicYearId) { <option [value]="y.academicYearId" [selected]="String(y.academicYearId) === yearId()">{{ y.yearName }}{{ y.isCurrent ? ' (current)' : '' }}</option> }
                </select>
              </label>
            </div>
            <div class="presets" role="radiogroup" aria-label="Preset">
              @for (p of presets(); track p.code) {
                <button type="button" role="radio" class="preset" [class.on]="presetCode() === p.code" [attr.aria-checked]="presetCode() === p.code" (click)="choose(p)">
                  <strong>{{ p.name }}</strong>
                  <span class="note">{{ p.description }}</span>
                  <span class="chips">@for (t of p.terms; track t.name) { <span class="pill blue">{{ t.name }}: {{ examText(t) }}</span> }</span>
                  <span class="note"><small>{{ p.evaluationMode === 'MARKS' ? 'Marks' : 'Grades only' }} · grade scale: {{ p.scaleName }}{{ p.skills.length ? ' · skills: ' + p.skills.join(', ') : '' }}</small></span>
                </button>
              }
            </div>
          </app-card>
        }
        @case (2) {
          <app-card title="2. Which classes use it?">
            <p class="note">Every subject of the class (from Class-Subject Mapping) is added automatically. A class can be in one scheme per academic year.</p>
            <ul class="class-list">
              @for (c of classes(); track c.classId) {
                <li><label class="check"><input type="checkbox" [checked]="picked().has(c.classId)" (change)="toggleClass(c.classId, $any($event.target).checked)" /> {{ c.className }}</label></li>
              }
            </ul>
            @if (classes().length === 0) { <p class="note">There are no classes yet. Add them under School Setup, or use Import Masters.</p> }
          </app-card>
        }
        @case (3) {
          <app-card title="3. Review">
            <div class="grid-2">
              <label class="field"><span>Scheme name</span><input type="text" maxlength="80" [value]="name()" (input)="name.set($any($event.target).value)" aria-label="Scheme name" /></label>
              <label class="field"><span>Card layout</span>
                <select [value]="template()" (change)="template.set($any($event.target).value)" aria-label="Card layout">
                  @for (t of compatible(); track t.key) { <option [value]="t.key" [selected]="t.key === template()">{{ t.name }}{{ t.implemented ? '' : ' (printing comes later)' }}</option> }
                </select>
              </label>
              @if (preset()?.evaluationMode === 'MARKS') {
                <label class="field"><span>Percentage rounding</span>
                  <select [value]="rounding()" (change)="rounding.set($any($event.target).value)" aria-label="Percentage rounding">
                    <option value="ROUND_HALF_UP" [selected]="rounding() === 'ROUND_HALF_UP'">Round (93.636 becomes 93.64)</option>
                    <option value="TRUNCATE" [selected]="rounding() === 'TRUNCATE'">Cut off (93.636 becomes 93.63)</option>
                  </select>
                </label>
                <label class="field"><span>Pass mark per subject (%)</span><input type="number" min="0" max="100" [value]="subjectPass()" (input)="subjectPass.set($any($event.target).value)" aria-label="Subject pass mark" /></label>
                <label class="field"><span>Pass mark overall (%)</span><input type="number" min="0" max="100" [value]="overallPass()" (input)="overallPass.set($any($event.target).value)" aria-label="Overall pass mark" /></label>
              }
              <label class="check rank"><input type="checkbox" [checked]="rank()" (change)="rank.set($any($event.target).checked)" /> Show rank on the card</label>
            </div>
            <h3>Term dates <small class="note">(attendance is counted between them)</small></h3>
            <div class="table-wrap">
              <table class="dense" aria-label="Term dates">
                <thead><tr><th>Term</th><th>From</th><th>To</th></tr></thead>
                <tbody>
                  @for (t of preset()?.terms ?? []; track t.name; let i = $index) {
                    <tr>
                      <td>{{ t.name }}</td>
                      <td><input type="date" [value]="dates()[i]?.startDate" (change)="setDate(i, 'startDate', $any($event.target).value)" [attr.aria-label]="t.name + ' from'" /></td>
                      <td><input type="date" [value]="dates()[i]?.endDate" (change)="setDate(i, 'endDate', $any($event.target).value)" [attr.aria-label]="t.name + ' to'" /></td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <p class="note">You will be able to change everything afterwards, add or remove subjects and exams, and set exam dates.</p>
          </app-card>
        }
      }

      <div class="nav">
        <app-button variant="secondary" [disabled]="step() === 1 || saving()" (click)="step.set(step() - 1)">Back</app-button>
        @if (step() < 3) {
          <app-button [disabled]="!canNext()" (click)="next()">Next</app-button>
        } @else {
          <app-button [disabled]="saving() || !canCreate()" (click)="create()">{{ saving() ? 'Creating…' : 'Create scheme' }}</app-button>
        }
        <a routerLink="/examinations/schemes" class="cancel">Cancel</a>
      </div>
    }
  `,
  styles: `
    .steps { display: flex; gap: var(--space-5); list-style: none; padding: 0; margin: 0 0 var(--space-5); flex-wrap: wrap; }
    .steps li { display: flex; align-items: center; gap: var(--space-2); color: var(--color-text-muted); }
    .steps li span { display: inline-flex; align-items: center; justify-content: center; width: 26px; height: 26px; border-radius: 50%; border: 1px solid var(--color-border); font-size: var(--text-sm); }
    .steps li.on { color: var(--color-text); font-weight: var(--weight-semibold); }
    .steps li.on span, .steps li.done span { background: var(--color-primary); border-color: var(--color-primary); color: #fff; }
    .top { margin-bottom: var(--space-5); }
    .presets { display: grid; gap: var(--space-4); }
    .preset { display: flex; flex-direction: column; gap: var(--space-2); text-align: left; padding: var(--space-4) var(--space-5); border: 1px solid var(--color-border); border-radius: var(--radius-lg); background: var(--color-surface, #fff); font: inherit; cursor: pointer; }
    .preset.on { border-color: var(--color-primary); box-shadow: 0 0 0 2px var(--color-primary-soft); }
    .preset:focus-visible { outline: 2px solid var(--color-primary); outline-offset: 2px; }
    .chips { display: flex; flex-wrap: wrap; gap: var(--space-2); }
    .class-list { list-style: none; margin: var(--space-4) 0 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(160px, 1fr)); gap: var(--space-3); }
    .rank { align-self: end; height: 36px; }
    h3 { margin: var(--space-5) 0 var(--space-3); font-size: var(--text-md, 1rem); }
    .nav { display: flex; align-items: center; gap: var(--space-3); margin-top: var(--space-5); }
    .cancel { margin-left: auto; color: var(--color-text-muted); }
  `,
})
export class SchemeFromPresetComponent implements OnInit {
  protected readonly router = inject(Router);
  private readonly service = inject(ExaminationsService);
  private readonly yearsApi = inject(AcademicYearService);
  private readonly classesApi = inject(ClassService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly String = String;
  protected readonly breadcrumbs = [{ label: 'Examinations' }, { label: 'Report Schemes', link: '/examinations/schemes' }, { label: 'New' }];
  protected readonly stepNames = ['Preset', 'Classes', 'Review'];

  protected readonly step = signal(1);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly saving = signal(false);
  protected readonly presets = signal<SchemePreset[]>([]);
  protected readonly templates = signal<ReportTemplate[]>([]);
  protected readonly years = signal<AcademicYear[]>([]);
  protected readonly classes = signal<SchoolClass[]>([]);

  protected readonly yearId = signal('');
  protected readonly presetCode = signal('');
  protected readonly picked = signal<ReadonlySet<number>>(new Set());
  protected readonly name = signal('');
  protected readonly template = signal('');
  protected readonly rounding = signal('ROUND_HALF_UP');
  protected readonly rank = signal(false);
  protected readonly subjectPass = signal('33');
  protected readonly overallPass = signal('33');
  protected readonly dates = signal<{ startDate: string; endDate: string }[]>([]);

  protected readonly preset = computed(() => this.presets().find((p) => p.code === this.presetCode()) ?? null);
  protected readonly year = computed(() => this.years().find((y) => String(y.academicYearId) === this.yearId()) ?? null);
  protected readonly compatible = computed(() => this.templates().filter((t) => !this.preset() || t.modes.includes(this.preset()!.evaluationMode)));
  protected readonly canNext = computed(() => (this.step() === 1 ? !!this.preset() && !!this.year() : this.picked().size > 0));
  protected readonly canCreate = computed(() => !!this.name().trim() && !!this.template() && this.dates().every((d) => d.startDate && d.endDate && d.endDate >= d.startDate));

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    forkJoin({ presets: this.service.schemePresets(), templates: this.service.templates(), years: this.yearsApi.list(), classes: this.classesApi.list() })
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.presets.set(r.presets);
          this.templates.set(r.templates);
          const years = r.years.filter((y) => (y.statusName ?? 'Active') === 'Active');
          this.years.set(years);
          this.classes.set(r.classes.filter((c) => (c.statusName ?? 'Active') === 'Active').sort((a, b) => a.sequenceOrder - b.sequenceOrder));
          this.yearId.set(String((years.find((y) => y.isCurrent) ?? years[0])?.academicYearId ?? ''));
          if (r.presets.length) this.choose(r.presets[0]);
        },
        error: () => this.failed.set(true),
      });
  }

  protected examText(t: { exams: { name: string; maxMarks: number }[] }): string {
    return t.exams.map((e) => `${e.name} ${e.maxMarks}`).join(' + ');
  }

  protected choose(p: SchemePreset): void {
    this.presetCode.set(p.code);
    this.template.set(p.templateKey);
    this.rank.set(p.showRank);
  }

  protected toggleClass(id: number, on: boolean): void {
    this.picked.update((s) => { const n = new Set(s); if (on) n.add(id); else n.delete(id); return n; });
  }

  protected setDate(i: number, field: 'startDate' | 'endDate', value: string): void {
    this.dates.update((d) => d.map((x, j) => (j === i ? { ...x, [field]: value } : x)));
  }

  protected next(): void {
    if (this.step() === 2) this.prepareReview();
    this.step.set(this.step() + 1);
  }

  /** Fills in what step 3 shows: a name, and the academic year split evenly into the preset's terms. */
  private prepareReview(): void {
    const p = this.preset()!;
    const y = this.year()!;
    if (!this.name().trim()) this.name.set(`${p.name} ${y.yearName}`.slice(0, 80));
    this.dates.set(splitYear(y.startDate, y.endDate, p.terms.length));
  }

  protected create(): void {
    const p = this.preset();
    if (!p || this.saving() || !this.canCreate()) return;
    const body: SchemeFromPresetRequest = {
      presetCode: p.code, academicYearId: Number(this.yearId()), schemeName: this.name().trim(), classIds: [...this.picked()], termDates: this.dates(), templateKey: this.template(), showRank: this.rank(),
      ...(p.evaluationMode === 'MARKS' ? { percentageRounding: this.rounding(), subjectPassPercent: Number(this.subjectPass()), overallPassPercent: Number(this.overallPass()) } : {}),
    };
    this.saving.set(true);
    this.service.fromPreset(body).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (s) => { this.toast.success('Report scheme created. Check the details, then set the exam dates.'); void this.router.navigate(['/examinations/schemes', s.schemeId]); },
      error: () => undefined, // already toasted (e.g. a class is already in another scheme this year)
    });
  }
}
