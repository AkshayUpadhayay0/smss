import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { EMPTY, Subject, catchError, debounceTime, finalize, forkJoin, map, merge, of, switchMap, tap } from 'rxjs';
import { ROLES } from '../../../../core/config/route-roles';
import { extractErrorMessage } from '../../../../core/interceptors/error.interceptor';
import { AuthService } from '../../../../core/services/auth.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, TextareaComponent } from '../../../../shared/components';
import { ColorFieldComponent } from '../../../school-setup/components/color-field/color-field.component';
import { HEX_COLOR, RC_LIMITS, ReportCardDesign, ReportCardPreview, ReportCardSchemeChoice, ReportCardSettings, ReportCardSettingsRequest } from '../../models/report-card-settings.model';
import { ReportCardSettingsService } from '../../services/report-card-settings.service';

const SIGNATURE_TYPES = ['image/png', 'image/jpeg', 'image/webp'];
const SIGNATURE_MAX_BYTES = 2 * 1024 * 1024;

/** Message from either failure shape of the API wrapper: an HTTP error, or the unwrapped `{ status: false, message }` envelope. */
function failureMessage(e: unknown, fallback: string): string {
  if (e instanceof HttpErrorResponse) return extractErrorMessage(e);
  const m = (e as { message?: unknown } | null)?.message;
  return typeof m === 'string' && m ? m : fallback;
}

/**
 * Examinations → Report Card Settings: the school's report card look (colours, header, recognition, UDISE, footer, rules, signature, cover and
 * rules pages, attendance and legend), a live gallery of the built designs drawn by the server in those colours, and which design each report
 * scheme prints with. The School Admin edits; the Principal sees it read-only. Published cards never change when these are edited.
 */
@Component({
  selector: 'app-report-card-settings',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, ColorFieldComponent, IconComponent, InputComponent, PageHeaderComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './report-card-settings.component.scss',
  templateUrl: './report-card-settings.component.html',
})
export class ReportCardSettingsComponent implements OnInit {
  private readonly service = inject(ReportCardSettingsService);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Examinations' }, { label: 'Report Card Settings' }];
  protected readonly limits = RC_LIMITS;
  protected readonly canEdit = computed(() => this.auth.currentUserRoles().includes(ROLES.SCHOOL_ADMIN));

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly signatureBusy = signal(false);
  protected readonly schemeBusy = signal<number>(0);

  protected readonly designs = signal<ReportCardDesign[]>([]);
  protected readonly schemes = signal<ReportCardSchemeChoice[]>([]);
  protected readonly saved = signal<ReportCardSettings | null>(null);
  protected readonly hasSignature = computed(() => this.saved()?.hasSignature ?? false);
  protected readonly hasLogo = computed(() => this.saved()?.hasLogo ?? false);
  protected readonly isDefault = computed(() => this.saved()?.isDefault ?? true);
  protected readonly schoolName = computed(() => this.saved()?.schoolName ?? '');

  protected readonly form = new FormGroup({
    primaryColor: new FormControl('#1F3A5F', { nonNullable: true, validators: [Validators.pattern(HEX_COLOR)] }),
    secondaryColor: new FormControl('#C8963E', { nonNullable: true, validators: [Validators.pattern(HEX_COLOR)] }),
    headerLine: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(RC_LIMITS.headerLine)] }),
    recognitionText: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(RC_LIMITS.recognitionText)] }),
    udiseCode: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(RC_LIMITS.udiseCode)] }),
    footerText: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(RC_LIMITS.footerText)] }),
    rulesText: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(RC_LIMITS.rulesText)] }),
    showCover: new FormControl(false, { nonNullable: true }),
    showRules: new FormControl(false, { nonNullable: true }),
    showAttendance: new FormControl(true, { nonNullable: true }),
    showGradeLegend: new FormControl(true, { nonNullable: true }),
    showPhoto: new FormControl(false, { nonNullable: true }),
  });
  // a computed() cannot see `form.valid` or a silent reset, so the value and validity are mirrored into signals
  private readonly value = signal(this.form.getRawValue());
  private readonly formValid = signal(true);
  private syncForm(): void {
    this.value.set(this.form.getRawValue());
    this.formValid.set(this.form.valid);
  }

  protected readonly dirty = computed(() => {
    const s = this.saved();
    if (!s) return false;
    const v = this.value();
    const n = (x: string | null | undefined) => (x ?? '').trim();
    return (
      v.primaryColor?.toUpperCase() !== s.primaryColor.toUpperCase() ||
      v.secondaryColor?.toUpperCase() !== s.secondaryColor.toUpperCase() ||
      n(v.headerLine) !== n(s.headerLine) ||
      n(v.recognitionText) !== n(s.recognitionText) ||
      n(v.udiseCode) !== n(s.udiseCode) ||
      n(v.footerText) !== n(s.footerText) ||
      n(v.rulesText) !== n(s.rulesText) ||
      v.showCover !== s.showCover ||
      v.showRules !== s.showRules ||
      v.showAttendance !== s.showAttendance ||
      v.showGradeLegend !== s.showGradeLegend ||
      v.showPhoto !== (s.showPhoto ?? false)
    );
  });
  /** The very first save of a school that has never saved settings is allowed even when nothing differs from the defaults. */
  protected readonly canSave = computed(() => this.canEdit() && (this.dirty() || this.isDefault()) && this.formValid() && !this.saving());
  protected readonly rulesLength = computed(() => String(this.value().rulesText ?? '').length);

  // ── live previews (real server renders) ──────────────────────────────
  protected readonly terms = signal(2);
  protected readonly chosen = signal('');
  protected readonly previews = signal<Record<string, ReportCardPreview | null>>({});
  protected readonly previewing = signal(false);
  private readonly refresh$ = new Subject<void>();
  protected readonly available = computed(() => this.designs().filter((d) => d.available));
  protected readonly chosenDesign = computed(() => this.designs().find((d) => d.code === this.chosen()) ?? null);
  /** The photo switch is only shown when the design being looked at has a photo slot (the others ignore it). */
  protected readonly photoSupported = computed(() => this.chosenDesign()?.supportsPhoto === true);
  /** Which kind of sample a design for both modes is drawn with. A design for one mode always shows its own. */
  protected readonly sampleMode = signal<'MARKS' | 'GRADES'>('MARKS');
  protected readonly bothModes = computed(() => (this.chosenDesign()?.modes.length ?? 0) > 1);

  ngOnInit(): void {
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.syncForm());
    // re-render once the school stops typing or dragging; an older render in flight is dropped
    merge(this.form.valueChanges, this.refresh$)
      .pipe(debounceTime(350), switchMap(() => this.renderAll()), takeUntilDestroyed(this.destroyRef))
      .subscribe();
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .get()
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (s) => {
          this.designs.set(s.designs);
          this.schemes.set(s.schemes);
          if (!this.chosen()) this.chosen.set(s.designs.find((d) => d.available)?.code ?? s.designs[0]?.code ?? '');
          this.apply(s);
          if (!this.canEdit()) this.form.disable({ emitEvent: false });
          this.refresh$.next();
        },
        error: () => this.loadFailed.set(true),
      });
  }

  private apply(s: ReportCardSettings): void {
    this.saved.set(s);
    this.form.reset(
      {
        primaryColor: s.primaryColor, secondaryColor: s.secondaryColor, headerLine: s.headerLine ?? '', recognitionText: s.recognitionText ?? '', udiseCode: s.udiseCode ?? '',
        footerText: s.footerText ?? '', rulesText: s.rulesText ?? '', showCover: s.showCover, showRules: s.showRules, showAttendance: s.showAttendance, showGradeLegend: s.showGradeLegend, showPhoto: s.showPhoto ?? false,
      },
      { emitEvent: false },
    );
    this.syncForm();
  }

  private request(): ReportCardSettingsRequest {
    const v = this.form.getRawValue();
    const text = (x: string) => (x.trim() ? x.trim() : null);
    return {
      primaryColor: v.primaryColor, secondaryColor: v.secondaryColor, headerLine: text(v.headerLine), recognitionText: text(v.recognitionText), udiseCode: text(v.udiseCode),
      footerText: text(v.footerText), rulesText: text(v.rulesText), showCover: v.showCover, showRules: v.showRules, showAttendance: v.showAttendance, showGradeLegend: v.showGradeLegend, showPhoto: v.showPhoto,
    };
  }

  private renderAll() {
    const list = this.available();
    if (list.length === 0 || this.form.invalid) return EMPTY;
    this.previewing.set(true);
    const settings = this.request();
    return forkJoin(list.map((d) => this.service.preview({ templateKey: d.code, settings, terms: this.terms(), ...(d.modes.length > 1 ? { mode: this.sampleMode() } : {}) }).pipe(catchError(() => of(null))))).pipe(
      map((results) => Object.fromEntries(list.map((d, i) => [d.code, results[i]]))),
      tap((byCode) => this.previews.set(byCode)),
      finalize(() => this.previewing.set(false)),
    );
  }

  /** The first page of a design, as an image source (the gallery thumbnail). */
  protected thumb(code: string): string | null {
    const p = this.previews()[code];
    return p?.pages[0] ? `data:image/png;base64,${p.pages[0]}` : null;
  }
  protected pages(code: string): string[] {
    return (this.previews()[code]?.pages ?? []).map((b) => `data:image/png;base64,${b}`);
  }
  protected failed(code: string): boolean {
    return code in this.previews() && this.previews()[code] === null && !this.previewing();
  }
  protected modeLabel(d: ReportCardDesign): string {
    return d.modes.map((m) => (m === 'MARKS' ? 'Marks' : 'Grades')).join(' + ');
  }
  protected landscape(d: ReportCardDesign | null): boolean {
    return !!d && d.orientation.toLowerCase() === 'landscape';
  }

  protected choose(code: string): void {
    const d = this.designs().find((x) => x.code === code);
    if (d?.available) this.chosen.set(code);
  }
  protected setSampleMode(mode: 'MARKS' | 'GRADES'): void {
    if (mode === this.sampleMode()) return;
    this.sampleMode.set(mode);
    this.refresh$.next();
  }
  protected setTerms(n: number): void {
    if (n === this.terms()) return;
    this.terms.set(n);
    this.refresh$.next();
  }
  protected setColor(which: 'primaryColor' | 'secondaryColor', value: string): void {
    if (this.canEdit()) this.form.controls[which].setValue(value);
  }

  protected save(): void {
    if (!this.canSave()) return;
    this.saving.set(true);
    this.service
      .save(this.request())
      .pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (s) => {
          this.apply(s);
          this.designs.set(s.designs);
          this.schemes.set(s.schemes);
          this.toast.success('Report card settings saved. Cards published from now on use them; published cards do not change.');
        },
        error: () => undefined, // the server's reason is toasted by the interceptor; the form keeps what was typed
      });
  }

  // ── per-scheme design ────────────────────────────────────────────────
  /** A design can be chosen for a scheme when it is built and draws that scheme's evaluation mode. */
  protected fits(d: ReportCardDesign, s: ReportCardSchemeChoice): boolean {
    return d.available && d.modes.includes(s.evaluationMode);
  }
  protected optionLabel(d: ReportCardDesign, s: ReportCardSchemeChoice): string {
    if (!d.available) return `${d.name} (coming soon)`;
    return d.modes.includes(s.evaluationMode) ? d.name : `${d.name} (not for this scheme)`;
  }

  protected onSchemeDesign(s: ReportCardSchemeChoice, event: Event): void {
    const select = event.target as HTMLSelectElement;
    const code = select.value;
    const d = this.designs().find((x) => x.code === code);
    if (!d || !this.fits(d, s) || code === s.templateKey) {
      select.value = s.templateKey;
      return;
    }
    this.schemeBusy.set(s.schemeId);
    this.service
      .setSchemeDesign(s.schemeId, code)
      .pipe(finalize(() => this.schemeBusy.set(0)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.schemes.update((list) => list.map((x) => (x.schemeId === s.schemeId ? { ...x, templateKey: code } : x)));
          this.toast.success(`${s.schemeName} will print with ${d.name}.`);
        },
        error: () => {
          select.value = s.templateKey;
        },
      });
  }

  // ── signature ────────────────────────────────────────────────────────
  protected onSignature(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // the same file can be chosen again
    if (!file) return;
    if (!SIGNATURE_TYPES.includes(file.type)) return void this.toast.error('The signature must be a PNG, JPG or WEBP image.');
    if (file.size > SIGNATURE_MAX_BYTES) return void this.toast.error('The signature image must be 2 MB or smaller.');
    this.signatureBusy.set(true);
    this.service
      .uploadSignature(file)
      .pipe(finalize(() => this.signatureBusy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.saved.update((s) => (s ? { ...s, hasSignature: true } : s));
          this.refresh$.next();
          this.toast.success('Signature uploaded.');
        },
        error: (e: unknown) => this.toast.error(failureMessage(e, 'The signature could not be uploaded.')),
      });
  }

  protected removeSignature(): void {
    this.signatureBusy.set(true);
    this.service
      .removeSignature()
      .pipe(finalize(() => this.signatureBusy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.saved.update((s) => (s ? { ...s, hasSignature: false } : s));
          this.refresh$.next();
          this.toast.success('Signature removed.');
        },
        error: (e: unknown) => this.toast.error(failureMessage(e, 'The signature could not be removed.')),
      });
  }

  protected err(name: 'headerLine' | 'recognitionText' | 'udiseCode' | 'footerText' | 'rulesText'): string | undefined {
    const c = this.form.controls[name];
    return c.errors?.['maxlength'] ? `Too long (max ${c.errors['maxlength'].requiredLength} characters).` : undefined;
  }
}
