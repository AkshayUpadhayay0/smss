import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, forkJoin, map, of, switchMap, tap } from 'rxjs';
import { MasterDataService } from '../../../../core/services/master-data.service';
import { ToastService } from '../../../../core/services/toast.service';
import {
  Breadcrumb,
  ButtonComponent,
  CardComponent,
  IconComponent,
  InputComponent,
  PageHeaderComponent,
  SelectComponent,
  SelectOption,
} from '../../../../shared/components';
import { LogoChange, LogoUploadComponent } from '../../components/logo-upload/logo-upload.component';
import { RegistrationResultComponent } from '../../components/registration-result/registration-result.component';
import { CONTACT_TYPES, SchoolRegistrationResponse, SchoolsListModel } from '../../models/school.model';
import { SchoolService } from '../../services/school.service';
import { ContactGroup, SchoolForm, createContactGroup, createSchoolForm, schoolToFormValue, toCreateRequest, toUpdateRequest } from '../../utils/school-form';
import { firstErrorMessage } from '../../utils/school-validators';

export type SchoolFormMode = 'add' | 'edit' | 'view';

const TITLES: Record<SchoolFormMode, { title: string; subtitle: string }> = {
  add: { title: 'Register School', subtitle: 'Create a new school and its login account' },
  edit: { title: 'Edit School', subtitle: 'Update school details' },
  view: { title: 'School Details', subtitle: 'Read-only view of this school' },
};

@Component({
  selector: 'app-school-form',
  imports: [
    ReactiveFormsModule,
    ButtonComponent,
    CardComponent,
    IconComponent,
    InputComponent,
    PageHeaderComponent,
    SelectComponent,
    LogoUploadComponent,
    RegistrationResultComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './school-form.component.scss',
  templateUrl: './school-form.component.html',
})
export class SchoolFormComponent implements OnInit {
  private readonly schools = inject(SchoolService);
  private readonly master = inject(MasterDataService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Bound from route data / params (withComponentInputBinding). */
  readonly mode = input<SchoolFormMode>('add');
  readonly schoolId = input<string>();

  protected readonly form = createSchoolForm();
  protected readonly contacts = this.form.controls.contacts;

  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);
  protected readonly school = signal<SchoolsListModel | null>(null);
  protected readonly created = signal<SchoolRegistrationResponse | null>(null);
  private logoChange: LogoChange = { file: null, remove: false };

  protected readonly isAdd = computed(() => this.mode() === 'add');
  protected readonly isView = computed(() => this.mode() === 'view');
  protected readonly heading = computed(() => TITLES[this.mode()]);
  protected readonly breadcrumbs = computed<Breadcrumb[]>(() => [{ label: 'Registered Schools', link: '/schools' }, { label: TITLES[this.mode()].title }]);
  protected readonly logoUrl = computed(() => this.schools.toLogoUrl(this.school()?.logoUrl));

  // ── Lookup options ──────────────────────────────────────────────────
  protected readonly contactTypeOptions: SelectOption[] = CONTACT_TYPES.map((t) => ({ label: t, value: t }));
  protected readonly schoolTypeOptions = toSignal(this.safe(this.master.getSchoolTypeOptions()), { initialValue: [] });
  protected readonly schoolLevelOptions = toSignal(this.safe(this.master.getSchoolLevelOptions()), { initialValue: [] });
  protected readonly boardTypeOptions = toSignal(this.safe(this.master.getBoardTypeOptions()), { initialValue: [] });
  protected readonly subscriptionStatusOptions = toSignal(this.safe(this.master.getSubscriptionStatusOptions()), { initialValue: [] });
  protected readonly countryOptions = toSignal(this.safe(this.master.getCountryOptions()), { initialValue: [] });
  protected readonly stateOptions = signal<SelectOption[]>([]);
  protected readonly districtOptions = signal<SelectOption[]>([]);
  protected readonly cityOptions = signal<SelectOption[]>([]);

  ngOnInit(): void {
    this.setupLocationCascade();
    this.setupDateRevalidation();
    this.setChildrenEnabled({ state: false, district: false, city: false });

    if (this.isAdd()) {
      // A primary contact is mandatory, so the form starts with one (pre-selected as primary).
      this.contacts.push(createContactGroup({ isPrimary: true }), { emitEvent: false });
      return;
    }

    // The UDISE rule is for NEW schools; legacy codes (e.g. "SUAD01") can't be changed, so they must not block saving.
    this.form.controls.schoolCode.disable({ emitEvent: false });

    const id = this.schoolId();
    if (!id) {
      this.loadFailed.set(true);
      return;
    }
    this.loadSchool(id);
  }

  // ── Loading (edit / view) ───────────────────────────────────────────
  private loadSchool(id: string): void {
    this.loading.set(true);
    this.schools
      .getSchool(id)
      .pipe(
        switchMap((school) => this.loadSavedLocationChain(school).pipe(map((chain) => ({ school, chain })))),
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ school, chain }) => {
          this.school.set(school);
          // The full saved chain is loaded first so the cascading selects pre-select correctly.
          this.stateOptions.set(chain.states);
          this.districtOptions.set(chain.districts);
          this.cityOptions.set(chain.cities);

          this.form.patchValue(schoolToFormValue(school), { emitEvent: false });
          this.contacts.clear({ emitEvent: false });
          school.contacts.forEach((c) => this.contacts.push(createContactGroup(c), { emitEvent: false }));

          this.setChildrenEnabled({
            state: !!school.countryId,
            district: !!school.stateId,
            city: !!school.districtId,
          });
          this.form.updateValueAndValidity({ emitEvent: false });
          if (this.isView()) this.form.disable({ emitEvent: false });
        },
        error: () => this.loadFailed.set(true), // already toasted by the error interceptor
      });
  }

  /** Parents are known from the saved school, so all three levels can load in parallel. */
  private loadSavedLocationChain(s: SchoolsListModel) {
    const none = of<SelectOption[]>([]);
    return forkJoin({
      states: s.countryId ? this.safe(this.master.getStateOptions(s.countryId)) : none,
      districts: s.countryId && s.stateId ? this.safe(this.master.getDistrictOptions(s.countryId, s.stateId)) : none,
      cities: s.countryId && s.stateId && s.districtId ? this.safe(this.master.getCityOptions(s.countryId, s.stateId, s.districtId)) : none,
    });
  }

  // ── Cascading location ──────────────────────────────────────────────
  // Changing a parent resets and disables everything below it. Programmatic updates use
  // { emitEvent: false } so they never re-trigger these pipelines.
  private setupLocationCascade(): void {
    const c = this.form.controls;
    const levels = {
      state: { control: c.stateId, options: this.stateOptions },
      district: { control: c.districtId, options: this.districtOptions },
      city: { control: c.cityId, options: this.cityOptions },
    };
    const reset = (...names: (keyof typeof levels)[]) => {
      for (const name of names) {
        levels[name].control.setValue('', { emitEvent: false });
        levels[name].control.markAsUntouched();
        levels[name].options.set([]);
      }
    };

    c.countryId.valueChanges
      .pipe(
        tap(() => {
          reset('state', 'district', 'city');
          this.setChildrenEnabled({ state: false, district: false, city: false });
        }),
        switchMap((country) => (country ? this.safe(this.master.getStateOptions(+country)) : of([] as SelectOption[]))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((options) => {
        this.stateOptions.set(options);
        this.setChildrenEnabled({ state: !!c.countryId.value, district: false, city: false });
      });

    c.stateId.valueChanges
      .pipe(
        tap(() => {
          reset('district', 'city');
          this.setChildrenEnabled({ state: true, district: false, city: false });
        }),
        switchMap((state) =>
          state ? this.safe(this.master.getDistrictOptions(+c.countryId.value, +state)) : of([] as SelectOption[]),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((options) => {
        this.districtOptions.set(options);
        this.setChildrenEnabled({ state: true, district: !!c.stateId.value, city: false });
      });

    c.districtId.valueChanges
      .pipe(
        tap(() => {
          reset('city');
          this.setChildrenEnabled({ state: true, district: true, city: false });
        }),
        switchMap((district) =>
          district ? this.safe(this.master.getCityOptions(+c.countryId.value, +c.stateId.value, +district)) : of([] as SelectOption[]),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((options) => {
        this.cityOptions.set(options);
        this.setChildrenEnabled({ state: true, district: true, city: !!c.districtId.value });
      });
  }

  private setChildrenEnabled(enabled: { state: boolean; district: boolean; city: boolean }): void {
    if (this.isView()) return; // the whole form is disabled in view mode
    const c = this.form.controls;
    const apply = (ctrl: AbstractControl, on: boolean) => (on ? ctrl.enable({ emitEvent: false }) : ctrl.disable({ emitEvent: false }));
    apply(c.stateId, enabled.state);
    apply(c.districtId, enabled.district);
    apply(c.cityId, enabled.city);
  }

  private setupDateRevalidation(): void {
    this.form.controls.subscriptionStartDate.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.form.controls.subscriptionEndDate.updateValueAndValidity());
  }

  // ── Contacts ────────────────────────────────────────────────────────
  protected addContact(): void {
    this.contacts.push(createContactGroup({ isPrimary: this.contacts.length === 0 }));
    this.contacts.markAsDirty();
  }

  /**
   * Saved contacts can't be removed (the API's update never deletes contacts), and the last contact can't be
   * removed because a primary contact is mandatory.
   */
  protected canRemove(group: ContactGroup): boolean {
    return !this.isView() && group.controls.contactId.value == null && this.contacts.length > 1;
  }

  protected removeContact(index: number): void {
    const removedPrimary = this.contacts.at(index).controls.isPrimary.value;
    this.contacts.removeAt(index);
    if (removedPrimary && this.contacts.length > 0) this.setPrimary(0);
    this.contacts.markAsDirty();
  }

  protected setPrimary(index: number): void {
    this.contacts.controls.forEach((g, i) => g.controls.isPrimary.setValue(i === index));
    this.contacts.markAsDirty();
  }

  protected primaryError(): string | undefined {
    const message = this.contacts.errors?.['primary'] as string | undefined;
    return message && (this.submitted() || this.contacts.dirty) ? message : undefined;
  }

  // ── Template helpers ────────────────────────────────────────────────
  protected err(name: keyof SchoolForm['controls']): string | undefined {
    return firstErrorMessage(this.form.controls[name], this.submitted());
  }

  protected cerr(group: ContactGroup, name: keyof ContactGroup['controls']): string | undefined {
    return firstErrorMessage(group.controls[name], this.submitted());
  }

  protected onLogoChanged(change: LogoChange): void {
    this.logoChange = change;
  }

  protected goToList(): void {
    void this.router.navigate(['/schools']);
  }

  protected goToEdit(): void {
    void this.router.navigate(['/schools', this.schoolId(), 'edit']);
  }

  // ── Submit ──────────────────────────────────────────────────────────
  protected submit(): void {
    if (this.saving() || this.isView()) return;

    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.toast.error((this.contacts.errors?.['primary'] as string | undefined) ?? 'Please fix the highlighted fields.');
      requestAnimationFrame(() =>
        this.host.nativeElement.querySelector('.control.invalid, .primary-error')?.scrollIntoView({ block: 'center', behavior: 'smooth' }),
      );
      return;
    }

    this.saving.set(true);
    (this.isAdd() ? this.register() : this.update()).pipe(finalize(() => this.saving.set(false))).subscribe();
  }

  /** Register first; the logo is a best-effort follow-up that must never fail the registration. */
  private register(): Observable<unknown> {
    const { file } = this.logoChange;
    return this.schools.register(toCreateRequest(this.form)).pipe(
      switchMap((result) =>
        file
          ? this.schools.uploadLogo(result.school.schoolId, file).pipe(
              map((school) => ({ ...result, school })),
              catchError((e) => {
                this.toast.warning(`${this.messageOf(e)} You can add the logo later from the edit page.`, 'School registered, but the logo was not uploaded');
                return of(result);
              }),
            )
          : of(result),
      ),
      tap((result) => {
        this.created.set(result);
        this.toast.success(`${result.school.schoolName} registered successfully.`);
        window.scrollTo({ top: 0 });
      }),
      catchError(() => of(null)), // the error interceptor already toasted; keep the form for another try
    );
  }

  /** Update, then apply the pending logo change (new file or removal). */
  private update(): Observable<unknown> {
    const id = this.schoolId()!;
    const { file, remove } = this.logoChange;
    return this.schools.update(id, toUpdateRequest(this.form)).pipe(
      switchMap((updated) => {
        const logo$ = remove ? this.schools.removeLogo(id) : file ? this.schools.uploadLogo(id, file) : null;
        return logo$
          ? logo$.pipe(
              catchError((e) => {
                this.toast.warning(this.messageOf(e), 'School updated, but the logo change failed');
                return of(updated);
              }),
            )
          : of(updated);
      }),
      tap((school) => {
        this.toast.success(`${school.schoolName} updated successfully.`);
        this.goToList();
      }),
      catchError(() => of(null)),
    );
  }

  private messageOf(e: unknown): string {
    const err = e as { error?: { message?: string }; message?: string };
    return err?.error?.message ?? err?.message ?? 'Something went wrong.';
  }

  /** A failed lookup shouldn't break the form: the field just stays empty (the interceptor already toasted). */
  private safe<T>(source: Observable<T[]>): Observable<T[]> {
    return source.pipe(catchError(() => of([] as T[])));
  }
}
