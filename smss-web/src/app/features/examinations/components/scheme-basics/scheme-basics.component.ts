import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent } from '../../../../shared/components';
import { AcademicYear } from '../../../school-setup/models/academic-year.model';
import { GradeScaleListItem, ReportTemplate, SaveSchemeRequest, Scheme } from '../../models/examinations.model';
import { ExaminationsService } from '../../services/examinations.service';

/** The scheme's header: name, year, mode, grade scale, pass rules, rounding, rank, attendance and the card layout. Also creates a blank scheme. */
@Component({
  selector: 'app-scheme-basics',
  imports: [ButtonComponent, CardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    <app-card [title]="scheme() ? 'Scheme details' : 'New blank scheme'">
      <div class="grid-2">
        <label class="field"><span>Name</span><input type="text" maxlength="80" [value]="name()" [disabled]="!canEdit()" (input)="name.set($any($event.target).value)" aria-label="Scheme name" /></label>
        <label class="field"><span>Academic year</span>
          <select [disabled]="!canEdit() || !!scheme()?.classes?.length || !!scheme()?.terms?.length" (change)="yearId.set($any($event.target).value)" aria-label="Academic year">
            @for (y of yearOptions(); track y.id) { <option [value]="y.id" [selected]="y.id === yearId()">{{ y.name }}</option> }
          </select>
          @if (scheme()?.classes?.length || scheme()?.terms?.length) { <span class="hint">Fixed once the scheme has classes or terms. Copy it into another year instead.</span> }
        </label>
        <label class="field"><span>Evaluation</span>
          <select [disabled]="!canEdit() || !!scheme()?.cells?.length" (change)="setMode($any($event.target).value)" aria-label="Evaluation mode">
            <option value="MARKS" [selected]="mode() === 'MARKS'">Marks (totals, percentage, result)</option>
            <option value="GRADES" [selected]="mode() === 'GRADES'">Grades only (no marks)</option>
          </select>
          @if (scheme()?.cells?.length) { <span class="hint">Fixed once the subject matrix is filled in.</span> }
        </label>
        <label class="field"><span>Grade scale</span>
          <select [disabled]="!canEdit()" (change)="scaleId.set($any($event.target).value)" aria-label="Grade scale">
            <option value="" [selected]="scaleId() === ''">{{ mode() === 'MARKS' ? 'No grade shown' : 'Choose a scale…' }}</option>
            @for (s of scaleOptions(); track s.gradeScaleId) { <option [value]="s.gradeScaleId" [selected]="String(s.gradeScaleId) === scaleId()">{{ s.scaleName }}</option> }
          </select>
          @if (scaleOptions().length === 0) { <span class="hint">No {{ mode() === 'MARKS' ? 'percentage' : 'rating' }} scale yet: add one under Grade Scales.</span> }
        </label>
        <label class="field"><span>Card layout</span>
          <select [disabled]="!canEdit()" (change)="template.set($any($event.target).value)" aria-label="Card layout">
            @for (t of compatible(); track t.key) { <option [value]="t.key" [selected]="t.key === template()">{{ t.name }} · {{ t.orientation }}{{ t.implemented ? '' : ' (printing comes later)' }}</option> }
          </select>
        </label>
        <label class="field"><span>Attendance on the card</span>
          <select [disabled]="!canEdit()" (change)="attendance.set($any($event.target).value)" aria-label="Attendance source">
            <option value="AUTO" [selected]="attendance() === 'AUTO'">Taken from SMSS attendance</option>
            <option value="MANUAL" [selected]="attendance() === 'MANUAL'">Typed in by the class teacher</option>
          </select>
        </label>
        @if (mode() === 'MARKS') {
          <label class="field"><span>Combine the terms</span>
            <select [disabled]="!canEdit()" (change)="combine.set($any($event.target).value)" aria-label="Year result">
              <option value="SUM_OF_TERMS" [selected]="combine() === 'SUM_OF_TERMS'">Add the terms together</option>
              <option value="WEIGHTED" [selected]="combine() === 'WEIGHTED'">Weighted by term</option>
            </select>
          </label>
          <label class="field"><span>Percentage rounding</span>
            <select [disabled]="!canEdit()" (change)="rounding.set($any($event.target).value)" aria-label="Percentage rounding">
              <option value="ROUND_HALF_UP" [selected]="rounding() === 'ROUND_HALF_UP'">Round (93.636 becomes 93.64)</option>
              <option value="TRUNCATE" [selected]="rounding() === 'TRUNCATE'">Cut off (93.636 becomes 93.63)</option>
            </select>
          </label>
          <label class="field"><span>Pass mark per subject (%)</span><input type="number" min="0" max="100" [value]="subjectPass()" [disabled]="!canEdit()" (input)="subjectPass.set($any($event.target).value)" aria-label="Subject pass mark" /></label>
          <label class="field"><span>Pass mark overall (%)</span><input type="number" min="0" max="100" [value]="overallPass()" [disabled]="!canEdit()" (input)="overallPass.set($any($event.target).value)" aria-label="Overall pass mark" /></label>
        }
        <label class="field"><span>Rank</span>
          <span class="check"><input type="checkbox" [checked]="rank()" [disabled]="!canEdit()" (change)="rank.set($any($event.target).checked)" /> Show rank on the card</span>
        </label>
        @if (rank() && mode() === 'MARKS') {
          <label class="field"><span>Rank among</span>
            <select [disabled]="!canEdit()" (change)="rankScope.set($any($event.target).value)" aria-label="Rank scope">
              <option value="SECTION" [selected]="rankScope() === 'SECTION'">The section</option>
              <option value="CLASS" [selected]="rankScope() === 'CLASS'">The whole class</option>
            </select>
          </label>
        }
      </div>
      @if (canEdit()) {
        <div class="row-actions" style="margin-top: var(--space-5)">
          <app-button [disabled]="saving() || !valid()" (click)="save()">{{ saving() ? 'Saving…' : scheme() ? 'Save details' : 'Create scheme' }}</app-button>
        </div>
      }
    </app-card>
  `,
})
export class SchemeBasicsComponent implements OnInit {
  private readonly service = inject(ExaminationsService);
  private readonly toast = inject(ToastService);

  protected readonly String = String;
  /** Null creates a blank scheme. */
  readonly scheme = input<Scheme | null>(null);
  readonly canEdit = input(false);
  readonly years = input<AcademicYear[]>([]);
  readonly scales = input<GradeScaleListItem[]>([]);
  readonly templates = input<ReportTemplate[]>([]);
  readonly saved = output<Scheme>();

  protected readonly name = signal('');
  protected readonly yearId = signal('');
  protected readonly mode = signal<'MARKS' | 'GRADES'>('MARKS');
  protected readonly scaleId = signal('');
  protected readonly template = signal('classic-card');
  protected readonly attendance = signal('AUTO');
  protected readonly combine = signal('SUM_OF_TERMS');
  protected readonly rounding = signal('ROUND_HALF_UP');
  protected readonly subjectPass = signal('33');
  protected readonly overallPass = signal('33');
  protected readonly rank = signal(false);
  protected readonly rankScope = signal('SECTION');
  protected readonly saving = signal(false);

  protected readonly yearOptions = computed(() => {
    const s = this.scheme();
    const list = this.years().map((y) => ({ id: String(y.academicYearId), name: y.yearName }));
    return s && !list.some((y) => y.id === String(s.academicYearId)) ? [{ id: String(s.academicYearId), name: s.academicYearName }, ...list] : list;
  });
  protected readonly scaleOptions = computed(() => this.scales().filter((s) => s.kind === (this.mode() === 'MARKS' ? 'PERCENT_BANDS' : 'RATING')));
  protected readonly compatible = computed(() => this.templates().filter((t) => t.modes.includes(this.mode())));
  protected readonly valid = computed(() => !!this.name().trim() && !!this.yearId() && !!this.template() && (this.mode() === 'MARKS' || !!this.scaleId()));

  ngOnInit(): void {
    const s = this.scheme();
    if (s) {
      this.name.set(s.schemeName); this.yearId.set(String(s.academicYearId)); this.mode.set(s.evaluationMode); this.scaleId.set(s.gradeScaleId == null ? '' : String(s.gradeScaleId));
      this.template.set(s.templateKey); this.attendance.set(s.attendanceSource); this.combine.set(s.yearCombine); this.rounding.set(s.percentageRounding);
      this.subjectPass.set(String(s.subjectPassPercent)); this.overallPass.set(String(s.overallPassPercent)); this.rank.set(s.showRank); this.rankScope.set(s.rankScope);
    } else {
      const current = this.years().find((y) => y.isCurrent) ?? this.years()[0];
      if (current) this.yearId.set(String(current.academicYearId));
    }
  }

  protected setMode(m: string): void {
    this.mode.set(m as 'MARKS' | 'GRADES');
    this.scaleId.set('');
    if (!this.compatible().some((t) => t.key === this.template())) this.template.set(this.compatible()[0]?.key ?? '');
  }

  protected save(): void {
    if (this.saving() || !this.valid()) return;
    const body: SaveSchemeRequest = {
      name: this.name().trim(), academicYearId: Number(this.yearId()), evaluationMode: this.mode(), gradeScaleId: this.scaleId() ? Number(this.scaleId()) : null, yearCombine: this.combine(),
      subjectPassPercent: Number(this.subjectPass()), overallPassPercent: Number(this.overallPass()), showRank: this.rank(), rankScope: this.rankScope(), attendanceSource: this.attendance(),
      percentageRounding: this.rounding(), templateKey: this.template(),
    };
    this.saving.set(true);
    const s = this.scheme();
    (s ? this.service.updateScheme(s.schemeId, body) : this.service.createScheme(body)).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (saved) => { this.toast.success(s ? 'Scheme details saved.' : 'Scheme created.'); this.saved.emit(saved); },
      error: () => undefined,
    });
  }
}
