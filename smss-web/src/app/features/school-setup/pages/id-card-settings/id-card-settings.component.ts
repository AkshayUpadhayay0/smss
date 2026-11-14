import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { EMPTY, Subject, catchError, debounceTime, filter, finalize, forkJoin, fromEvent, map, merge, of, switchMap, tap } from 'rxjs';
import { extractErrorMessage } from '../../../../core/interceptors/error.interceptor';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, TextareaComponent } from '../../../../shared/components';
import { ColorFieldComponent } from '../../components/color-field/color-field.component';
import {
  HEX_COLOR,
  IdCardPreview,
  IdCardSettings,
  IdCardSettingsRequest,
  IdCardTemplateInfo,
  MAX_TERMS_LENGTH,
  MAX_TERMS_LINES,
} from '../../models/id-card-settings.model';
import { IdCardSettingsService } from '../../services/id-card-settings.service';

const SIGNATURE_TYPES = ['image/png', 'image/jpeg', 'image/webp'];
const SIGNATURE_MAX_BYTES = 2 * 1024 * 1024;

/** A few short lines: at most 5 non-empty lines and 300 characters (the server enforces the same). */
function termsValidator(control: AbstractControl): ValidationErrors | null {
  const text = String(control.value ?? '');
  const lines = text.split(/\r?\n/).filter((l) => l.trim());
  if (lines.length > MAX_TERMS_LINES) return { termsLines: true };
  if (text.trim().length > MAX_TERMS_LENGTH) return { termsLength: true };
  return null;
}

const noSpaces: (c: AbstractControl) => ValidationErrors | null = (c) => (/\s/.test(String(c.value ?? '').trim()) ? { spaces: true } : null);

/** Message from either failure shape of the API wrapper: an HTTP error, or the unwrapped `{ status: false, message }` envelope. */
function failureMessage(e: unknown, fallback: string): string {
  if (e instanceof HttpErrorResponse) return extractErrorMessage(e);
  const m = (e as { message?: unknown } | null)?.message;
  return typeof m === 'string' && m ? m : fallback;
}

/**
 * School Setup → ID Card Settings: pick one of the built-in designs, brand it with the school's two colours, add the printed details and the
 * principal signature. Every thumbnail is a real render from the server (the same code that prints the cards), refreshed as the school edits.
 */
@Component({
  selector: 'app-id-card-settings',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, ColorFieldComponent, IconComponent, InputComponent, PageHeaderComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './id-card-settings.component.scss',
  templateUrl: './id-card-settings.component.html',
})
export class IdCardSettingsComponent implements OnInit {
  private readonly service = inject(IdCardSettingsService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'School Setup' }, { label: 'ID Card Settings' }];
  protected readonly maxTerms = MAX_TERMS_LENGTH;

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly signatureBusy = signal(false);

  protected readonly templates = signal<IdCardTemplateInfo[]>([]);
  /** What the server has (the baseline for "unsaved changes"). */
  private readonly saved = signal<IdCardSettings | null>(null);
  protected readonly hasSignature = computed(() => this.saved()?.hasSignature ?? false);
  protected readonly isDefault = computed(() => this.saved()?.isDefault ?? true);

  protected readonly form = new FormGroup({
    templateCode: new FormControl('classic-blue', { nonNullable: true }),
    primaryColor: new FormControl('#1E5BD8', { nonNullable: true, validators: [Validators.pattern(HEX_COLOR)] }),
    secondaryColor: new FormControl('#F5A623', { nonNullable: true, validators: [Validators.pattern(HEX_COLOR)] }),
    tagline: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(120)] }),
    website: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(150), noSpaces] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.email, Validators.maxLength(150)] }),
    termsText: new FormControl('', { nonNullable: true, validators: [termsValidator] }),
    showDob: new FormControl(true, { nonNullable: true }),
    showAddress: new FormControl(true, { nonNullable: true }),
    showBloodGroup: new FormControl(true, { nonNullable: true }),
    showMotherName: new FormControl(true, { nonNullable: true }),
  });
  /**
   * The form's current value and validity as signals. They are refreshed on every edit AND after a silent reset (a loaded / saved row), because a
   * computed() cannot see `form.valid` or a reset made with emitEvent: false.
   */
  private readonly value = signal(this.form.getRawValue());
  private readonly formValid = signal(true);
  private syncForm(): void {
    this.value.set(this.form.getRawValue());
    this.formValid.set(this.form.valid);
  }

  protected readonly selected = computed(() => this.value().templateCode ?? 'classic-blue');
  protected readonly selectedTemplate = computed(() => this.templates().find((t) => t.code === this.selected()) ?? null);
  /** The school has chosen its own colours (they differ from the selected template's defaults). Picking another design then keeps them. */
  protected readonly customColors = computed(() => {
    const t = this.selectedTemplate();
    const v = this.value();
    return !!t && (v.primaryColor?.toUpperCase() !== t.defaultPrimaryColor.toUpperCase() || v.secondaryColor?.toUpperCase() !== t.defaultSecondaryColor.toUpperCase());
  });

  protected readonly dirty = computed(() => {
    const s = this.saved();
    if (!s) return false;
    const v = this.value();
    const norm = (x: string | null | undefined) => (x ?? '').trim();
    return (
      v.templateCode !== s.templateCode ||
      v.primaryColor?.toUpperCase() !== s.primaryColor.toUpperCase() ||
      v.secondaryColor?.toUpperCase() !== s.secondaryColor.toUpperCase() ||
      norm(v.tagline) !== norm(s.tagline) ||
      norm(v.website) !== norm(s.website) ||
      norm(v.email) !== norm(s.email) ||
      norm(v.termsText) !== norm(s.termsText) ||
      v.showDob !== s.showDob ||
      v.showAddress !== s.showAddress ||
      v.showBloodGroup !== s.showBloodGroup ||
      v.showMotherName !== s.showMotherName
    );
  });
  /** The very first save of a school that has never saved settings is allowed even when nothing differs from the defaults. */
  protected readonly canSave = computed(() => (this.dirty() || this.isDefault()) && this.formValid() && !this.saving());
  protected readonly termsLength = computed(() => String(this.value().termsText ?? '').length);

  // ── live previews (real server renders) ──────────────────────────────
  protected readonly previews = signal<Record<string, IdCardPreview | null>>({});
  protected readonly previewing = signal(false);
  private readonly refresh$ = new Subject<void>();

  ngOnInit(): void {
    // Re-render on every edit, once the school stops typing / dragging; an older render in flight is dropped.
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.syncForm());
    // The school logo is changed on another screen (School Profile): coming back to this tab re-renders, so a new logo shows without a page reload.
    const returned$ = typeof document === 'undefined' ? EMPTY : fromEvent(document, 'visibilitychange').pipe(filter(() => document.visibilityState === 'visible'));
    merge(this.form.valueChanges, this.refresh$, returned$)
      .pipe(
        debounceTime(350),
        switchMap(() => this.renderAll()),
        takeUntilDestroyed(this.destroyRef),
      )
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
          this.templates.set(s.templates);
          this.apply(s);
          this.refresh$.next();
        },
        error: () => this.loadFailed.set(true),
      });
  }

  private apply(s: IdCardSettings): void {
    this.saved.set(s);
    this.form.reset(
      {
        templateCode: s.templateCode, primaryColor: s.primaryColor, secondaryColor: s.secondaryColor, tagline: s.tagline ?? '', website: s.website ?? '', email: s.email ?? '',
        termsText: s.termsText ?? '', showDob: s.showDob, showAddress: s.showAddress, showBloodGroup: s.showBloodGroup, showMotherName: s.showMotherName,
      },
      { emitEvent: false },
    );
    this.syncForm();
  }

  /** The request for one template. Colours are the school's own when it customised them, else the template's defaults (sent as null). */
  private requestFor(code: string): IdCardSettingsRequest {
    const v = this.form.getRawValue();
    const custom = this.customColors() && this.form.controls.primaryColor.valid && this.form.controls.secondaryColor.valid;
    const text = (x: string) => (x.trim() ? x.trim() : null);
    return {
      templateCode: code,
      primaryColor: custom ? v.primaryColor : null,
      secondaryColor: custom ? v.secondaryColor : null,
      tagline: text(v.tagline), website: text(v.website), email: text(v.email), termsText: text(v.termsText),
      showDob: v.showDob, showAddress: v.showAddress, showBloodGroup: v.showBloodGroup, showMotherName: v.showMotherName,
    };
  }

  private renderAll() {
    const list = this.templates();
    if (list.length === 0 || this.form.invalid) return EMPTY;
    this.previewing.set(true);
    return forkJoin(list.map((t) => this.service.preview(this.requestFor(t.code)).pipe(catchError(() => of(null))))).pipe(
      map((results) => Object.fromEntries(list.map((t, i) => [t.code, results[i]]))),
      tap((byCode) => this.previews.set(byCode)),
      finalize(() => this.previewing.set(false)),
    );
  }

  protected src(code: string, side: 'front' | 'back'): string | null {
    const p = this.previews()[code];
    return p ? `data:image/png;base64,${side === 'front' ? p.frontPng : p.backPng}` : null;
  }

  protected select(code: string): void {
    if (code === this.selected()) return;
    const next = this.templates().find((t) => t.code === code);
    // A design change keeps colours the school picked; if it never customised them, the new design brings its own defaults.
    if (next && !this.customColors()) this.form.patchValue({ templateCode: code, primaryColor: next.defaultPrimaryColor, secondaryColor: next.defaultSecondaryColor });
    else this.form.patchValue({ templateCode: code });
  }

  protected setColor(which: 'primaryColor' | 'secondaryColor', value: string): void {
    this.form.controls[which].setValue(value);
  }

  protected save(): void {
    if (!this.canSave()) return;
    const v = this.form.getRawValue();
    const request = { ...this.requestFor(v.templateCode), primaryColor: v.primaryColor, secondaryColor: v.secondaryColor };
    this.saving.set(true);
    this.service
      .save(request)
      .pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (s) => {
          this.apply(s);
          this.toast.success('ID card settings saved. New cards will use this design.');
        },
        error: () => undefined, // the server's reason is toasted by the interceptor; the form keeps what was typed
      });
  }

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

  protected err(name: 'tagline' | 'website' | 'email' | 'termsText'): string | undefined {
    const c = this.form.controls[name];
    if (!c.errors) return undefined;
    if (c.errors['maxlength']) return `Too long (max ${c.errors['maxlength'].requiredLength} characters).`;
    if (c.errors['email']) return 'Enter a valid e-mail address.';
    if (c.errors['spaces']) return 'Use a single web address with no spaces.';
    if (c.errors['termsLines']) return `Keep it to ${MAX_TERMS_LINES} short lines.`;
    if (c.errors['termsLength']) return `At most ${MAX_TERMS_LENGTH} characters.`;
    return 'Check this value.';
  }
}
