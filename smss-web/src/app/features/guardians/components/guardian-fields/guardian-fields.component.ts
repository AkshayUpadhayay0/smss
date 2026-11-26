import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { catchError, finalize, of } from 'rxjs';
import { ButtonComponent, IconComponent, InputComponent, SelectComponent, SelectOption, WhatsAppFieldComponent } from '../../../../shared/components';
import { GUARDIAN_RELATIONSHIPS } from '../../../admissions/models/admission.model';
import { Section } from '../../../school-setup/models/section.model';
import { SectionService } from '../../../school-setup/services/section.service';
import { GuardianPicks, PickedGuardian, SiblingStudent } from '../../models/guardian.model';
import { GuardianService } from '../../services/guardian.service';

/** The controls of a guardian section (the application form and Add Existing Student have the same six). */
export type GuardianFormGroup = FormGroup;

/**
 * The guardian section of a new student: either type a new parent, or pick a SIBLING's parents. "Has a sibling in this school?" opens a search (its own class filter, a name); a student's parents are shown with a
 * tick each, and the admin chooses which is the primary guardian (or a new person typed below). A picked primary guardian is LINKED, not copied: their name and mobile (shown masked, never whole) are read-only here,
 * a WhatsApp number or an email already on record is not asked for again, and what is entered for a missing one is written to the shared record when the student is created. Changing a stored number is done on the
 * parent-details edit screen. The component keeps the form's controls in step (read-only ones are disabled) and reports the pick through {@link picksChange}.
 */
@Component({
  selector: 'app-guardian-fields',
  imports: [ReactiveFormsModule, ButtonComponent, IconComponent, InputComponent, SelectComponent, WhatsAppFieldComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './guardian-fields.component.scss',
  template: `
    <div class="sibling">
      <label class="toggle"><input type="checkbox" [checked]="open()" (change)="open.set(!open())" data-field="sibling-toggle" /> Has a sibling in this school?</label>
      @if (open()) {
        <div class="search" data-section="sibling-search">
          <app-select label="Sibling's class" placeholder="Any class" [options]="classOptions()" [formControl]="classControl" />
          <app-input label="Sibling's name" placeholder="At least 2 letters" [formControl]="searchControl" />
          <app-button variant="secondary" [disabled]="searching()" (click)="search()" data-action="sibling-search"><app-icon name="search" [size]="14" />{{ searching() ? 'Searching…' : 'Search' }}</app-button>
        </div>
        @if (searchError()) {
          <p class="hint" role="alert" data-note="sibling-error">{{ searchError() }}</p>
        } @else if (searched() && results().length === 0) {
          <p class="hint" data-note="sibling-none">No active student matches. You can type the parent below.</p>
        }
        @if (results().length > 0) {
          <ul class="results" aria-label="Students">
            @for (s of results(); track s.studentId) {
              <li [attr.data-student]="s.studentId">
                <div class="who"><strong>{{ s.studentName }}</strong><span class="meta">{{ s.className ? s.className + (s.sectionName ? ' – ' + s.sectionName : '') : '' }}</span></div>
                @for (g of s.guardians; track g.guardianId) {
                  <label class="parent">
                    <input type="checkbox" [checked]="isPicked(g.guardianId)" (change)="toggle(g)" [attr.data-guardian]="g.guardianId" />
                    <span>{{ g.name }} <span class="meta">· {{ g.relationship }} · {{ g.maskedMobile }}{{ g.hasWhatsApp ? '' : ' · no WhatsApp' }}{{ g.hasEmail ? '' : ' · no email' }}</span></span>
                  </label>
                }
              </li>
            }
          </ul>
        }
      }
    </div>

    @if (picks().length > 0) {
      <div class="picked" data-section="picked" role="group" aria-label="Linked parents">
        <p class="label">Linked parents (the same record is shared with the sibling; nothing is copied)</p>
        @for (g of picks(); track g.guardianId) {
          <div class="row" [attr.data-picked]="g.guardianId">
            <label><input type="radio" name="primaryGuardian" [checked]="primaryId() === g.guardianId" (change)="makePrimary(g.guardianId)" /> Primary guardian</label>
            <span>{{ g.name }} <span class="meta">· {{ g.relationship }} · {{ g.maskedMobile }}</span></span>
            <app-button variant="danger-ghost" size="sm" aria-label="Remove" (click)="remove(g.guardianId)"><app-icon name="x" [size]="14" /></app-button>
          </div>
        }
        <div class="row"><label><input type="radio" name="primaryGuardian" [checked]="primaryId() === null" (change)="makePrimary(null)" data-field="primary-new" /> A new person is the primary guardian (type below)</label></div>
      </div>
    }

    <div class="grid">
      @if (primary(); as p) {
        <div class="linked" data-note="linked-primary">
          <span class="label">Primary guardian</span>
          <strong>{{ p.name }}</strong>
          <span class="meta">{{ p.maskedMobile }} · name and mobile come from the guardian record (change them on the parent-details screen)</span>
        </div>
      } @else {
        <app-input label="Guardian name" [required]="true" [formControl]="c('guardianName')" [errorText]="err('guardianName')" />
      }
      <app-select label="Relationship" [required]="true" placeholder="Select…" [options]="relationshipOptions" [formControl]="c('guardianRelationship')" [errorText]="err('guardianRelationship')" />
      @if (!primary()) {
        <app-input label="Guardian mobile" type="tel" [required]="true" placeholder="10-digit mobile" [formControl]="c('guardianMobileNumber')" [errorText]="err('guardianMobileNumber')" />
      }
      @if (!primary() || !primary()!.hasWhatsApp) {
        <app-whatsapp-field label="Guardian WhatsApp" [number]="c('guardianWhatsAppNumber')" [same]="c('guardianWhatsAppSameAsMobile')" [error]="err('guardianWhatsAppNumber')" />
      } @else {
        <div class="linked" data-note="whatsapp-on-file"><span class="label">Guardian WhatsApp</span><span class="meta">On record: no need to enter it again.</span></div>
      }
      @if (!primary() || !primary()!.hasEmail) {
        <app-input label="Guardian email" type="email" prefixIcon="mail" [required]="true" [formControl]="c('guardianEmail')" [errorText]="err('guardianEmail')" />
      } @else {
        <div class="linked" data-note="email-on-file"><span class="label">Guardian email</span><span class="meta">On record: no need to enter it again.</span></div>
      }
    </div>
    @if (form().get('guardianConsent'); as consent) {
      <div class="consent" data-section="consent">
        <label><input type="checkbox" [formControl]="asControl(consent)" data-field="guardian-consent" /> The parent agrees to be contacted by WhatsApp and email about the account and the child. <span class="meta">(required)</span></label>
        @if (consent.invalid && consent.touched) { <p class="hint" role="alert" data-note="consent-error">Tick the box to confirm the parent has agreed.</p> }
      </div>
    }
  `,
})
export class GuardianFieldsComponent implements OnInit {
  private readonly guardianApi = inject(GuardianService);
  private readonly sections = inject(SectionService);
  private readonly destroyRef = inject(DestroyRef);

  readonly form = input.required<GuardianFormGroup>();
  protected c(name: string): FormControl { return this.form().get(name) as FormControl; }
  protected asControl(control: unknown): FormControl { return control as FormControl; }
  /** The form's error text for a control (the form owns the "show errors after submit" rule). */
  readonly errorOf = input<(name: string) => string | undefined>(() => undefined);
  /** The pick to start from (editing an application that already reuses guardians). */
  readonly initial = input<GuardianPicks | null>(null);
  readonly picksChange = output<GuardianPicks>();

  protected readonly relationshipOptions: SelectOption[] = GUARDIAN_RELATIONSHIPS.map((r) => ({ label: r.label, value: r.value }));
  protected readonly open = signal(false);
  protected readonly searching = signal(false);
  protected readonly searched = signal(false);
  protected readonly searchError = signal<string | undefined>(undefined);
  protected readonly results = signal<SiblingStudent[]>([]);
  protected readonly picks = signal<PickedGuardian[]>([]);
  /** The primary guardian's id, or null when a new person (typed in) is the primary guardian. */
  protected readonly primaryId = signal<number | null>(null);
  protected readonly primary = computed(() => this.picks().find((g) => g.guardianId === this.primaryId()) ?? null);
  protected readonly classControl = new FormControl('', { nonNullable: true });
  protected readonly searchControl = new FormControl('', { nonNullable: true });
  private readonly allSections = signal<Section[]>([]);
  protected readonly classOptions = computed<SelectOption[]>(() => {
    const seen = new Map<number, string>();
    for (const s of this.allSections()) if (s.statusName === 'Active' || s.statusName == null) seen.set(s.classId, s.className);
    return [...seen.entries()].map(([value, label]) => ({ value: String(value), label })).sort((a, b) => a.label.localeCompare(b.label, undefined, { numeric: true }));
  });

  constructor() {
    effect(() => {
      const initial = this.initial();
      if (!initial) return;
      untracked(() => {
        const all = [...(initial.primary ? [initial.primary] : []), ...initial.extras];
        this.picks.set(all);
        this.primaryId.set(initial.primary?.guardianId ?? null);
        this.open.set(all.length > 0);
        this.apply();
      });
    });
  }

  ngOnInit(): void {
    this.sections.list().pipe(catchError(() => of([] as Section[])), takeUntilDestroyed(this.destroyRef)).subscribe((l) => this.allSections.set(l ?? []));
  }

  protected err(name: string): string | undefined {
    return this.errorOf()(name);
  }

  protected isPicked(id: number): boolean {
    return this.picks().some((g) => g.guardianId === id);
  }

  protected search(): void {
    if (this.searching()) return;
    const term = this.searchControl.value.trim();
    const classId = Number(this.classControl.value) || undefined;
    this.searchError.set(undefined);
    if (!classId && term.length < 2) {
      this.searchError.set("Choose the sibling's class, or type at least 2 letters of the name.");
      return;
    }
    this.searching.set(true);
    this.guardianApi
      .siblings({ classId, search: term })
      .pipe(finalize(() => this.searching.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.results.set(r ?? []);
          this.searched.set(true);
        },
        error: () => this.searchError.set('The search could not be done. Try again.'),
      });
  }

  protected toggle(g: { guardianId: number; name: string; relationship: string; maskedMobile: string; hasWhatsApp: boolean; hasEmail: boolean }): void {
    if (this.isPicked(g.guardianId)) {
      this.remove(g.guardianId);
      return;
    }
    this.picks.update((l) => [...l, { guardianId: g.guardianId, name: g.name, relationship: g.relationship, maskedMobile: g.maskedMobile, hasWhatsApp: g.hasWhatsApp, hasEmail: g.hasEmail }]);
    if (this.picks().length === 1 && this.primaryId() === null) this.primaryId.set(g.guardianId);   // the first parent picked is the primary guardian unless the admin chooses otherwise
    this.apply();
  }

  protected remove(id: number): void {
    this.picks.update((l) => l.filter((g) => g.guardianId !== id));
    if (this.primaryId() === id) this.primaryId.set(this.picks()[0]?.guardianId ?? null);
    this.apply();
  }

  protected makePrimary(id: number | null): void {
    this.primaryId.set(id);
    this.apply();
  }

  /** Keeps the form in step with the pick: an existing primary guardian's name and mobile are not typed (their controls are switched off), and WhatsApp and email only when the record lacks them. */
  private apply(): void {
    const p = this.primary();
    const toggle = (name: string, on: boolean) => (on ? this.c(name).enable({ emitEvent: false }) : this.c(name).disable({ emitEvent: false }));
    toggle('guardianName', !p);
    toggle('guardianMobileNumber', !p);
    toggle('guardianEmail', !p || !p.hasEmail);
    toggle('guardianWhatsAppNumber', !p || !p.hasWhatsApp);
    toggle('guardianWhatsAppSameAsMobile', !p || !p.hasWhatsApp);
    if (p && !this.c('guardianRelationship').value) this.c('guardianRelationship').setValue(p.relationship);
    this.form().updateValueAndValidity({ emitEvent: false });
    const extras = this.picks().filter((g) => g.guardianId !== this.primaryId());
    this.picksChange.emit({ primary: p, extras });
  }
}
