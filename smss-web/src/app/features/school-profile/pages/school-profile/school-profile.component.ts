import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable, catchError, finalize, forkJoin, map, of, switchMap, tap } from 'rxjs';
import { ROLES } from '../../../../core/config/route-roles';
import { AuthService } from '../../../../core/services/auth.service';
import { MasterDataService } from '../../../../core/services/master-data.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent, InputComponent, SelectComponent, SelectOption, WhatsAppFieldComponent } from '../../../../shared/components';
import { LogoChange, LogoUploadComponent } from '../../../schools/components/logo-upload/logo-upload.component';
import { SchoolStats, SchoolsListModel } from '../../../schools/models/school.model';
import { SchoolService } from '../../../schools/services/school.service';
import { firstErrorMessage } from '../../../schools/utils/school-validators';
import { ProfileForm, createProfileForm, profileToFormValue, toProfileRequest } from '../../utils/profile-form';

const NO_LOGO_CHANGE: LogoChange = { file: null, remove: false };

/**
 * The school profile page, in two contexts:
 *  - `/school-profile`: the signed-in user's own school (resolved server-side from the token). Every school role may open it; only a
 *    School Admin gets Edit (the API refuses the write for anyone else). Roles other than School Admin receive a limited profile
 *    (no school type / level / board / tax numbers / status), so those fields are hidden for them.
 *  - `/schools/:schoolId/profile`: a Super Admin looking at any school, always read-only.
 */
@Component({
  selector: 'app-school-profile',
  imports: [ReactiveFormsModule, RouterLink, ButtonComponent, IconComponent, InputComponent, SelectComponent, LogoUploadComponent, WhatsAppFieldComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './school-profile.component.scss',
  templateUrl: './school-profile.component.html',
})
export class SchoolProfileComponent implements OnInit {
  private readonly schools = inject(SchoolService);
  private readonly master = inject(MasterDataService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Set only on the Super Admin route (/schools/:schoolId/profile). */
  readonly schoolId = input<string>();
  protected readonly isAdminView = computed(() => !!this.schoolId());
  private readonly isSchoolAdmin = computed(() => this.auth.currentUserRoles().some((r) => r.trim().toLowerCase() === ROLES.SCHOOL_ADMIN.toLowerCase()));
  /** Only a School Admin editing their own school; never the Super Admin view. */
  protected readonly canEdit = computed(() => !this.isAdminView() && this.isSchoolAdmin());
  /** The full record (type / level / board / tax / status) comes back only for the School Admin and the Super Admin. */
  protected readonly showFull = computed(() => this.canEdit() || this.isAdminView());
  protected readonly form = createProfileForm();

  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  /** False = read-only view of the saved profile; true = fields editable. */
  protected readonly editing = signal(false);
  protected readonly submitted = signal(false);
  protected readonly school = signal<SchoolsListModel | null>(null);
  /** null = not loaded (or failed): the cards show "—", never a made-up number. */
  protected readonly stats = signal<SchoolStats | null>(null);
  protected readonly statusName = signal('');
  protected readonly locationText = signal('');
  /** Bumped after each save to re-create the logo picker (clears its pending file). */
  protected readonly logoVersion = signal(0);
  private logoChange: LogoChange = NO_LOGO_CHANGE;

  protected readonly logoUrl = computed(() => this.schools.toLogoUrl(this.school()?.logoUrl));
  protected readonly typeName = computed(() => this.schoolTypeOptions().find((o) => o.value === String(this.school()?.schoolTypeId ?? ''))?.label ?? '');
  protected readonly isActive = computed(() => this.statusName().toLowerCase() === 'active');
  protected readonly statCards = computed(() => {
    const s = this.stats();
    const fmt = (n: number | undefined) => (n === undefined ? '—' : n.toLocaleString('en-IN'));
    return [
      { key: 'students', label: 'Total Students', icon: 'graduation-cap' as const, value: fmt(s?.totalStudents) },
      { key: 'employees', label: 'Total Employees', icon: 'users' as const, value: fmt(s?.totalEmployees) },
      { key: 'classes', label: 'Total Classes', icon: 'book-open' as const, value: fmt(s?.totalClasses) },
      { key: 'sections', label: 'Total Sections', icon: 'layers' as const, value: fmt(s?.totalSections) },
    ];
  });

  // ── Lookup options ──────────────────────────────────────────────────
  protected readonly schoolTypeOptions = toSignal(this.safe(this.master.getSchoolTypeOptions()), { initialValue: [] });
  protected readonly schoolLevelOptions = toSignal(this.safe(this.master.getSchoolLevelOptions()), { initialValue: [] });
  protected readonly boardTypeOptions = toSignal(this.safe(this.master.getBoardTypeOptions()), { initialValue: [] });
  protected readonly countryOptions = toSignal(this.safe(this.master.getCountryOptions()), { initialValue: [] });
  protected readonly stateOptions = signal<SelectOption[]>([]);
  protected readonly districtOptions = signal<SelectOption[]>([]);
  protected readonly cityOptions = signal<SelectOption[]>([]);

  ngOnInit(): void {
    // GSTIN and PAN are stored upper-case: show them that way as the user types
    for (const ctrl of [this.form.controls.schoolGstin, this.form.controls.schoolPan]) {
      ctrl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
        const up = v.toUpperCase();
        if (up !== v) ctrl.setValue(up, { emitEvent: false });
      });
    }
    this.setupLocationCascade();
    this.setChildrenEnabled({ state: false, district: false, city: false });
    this.load();
  }

  protected load(): void {
    this.editing.set(false);
    this.loading.set(true);
    this.loadFailed.set(false);
    const id = this.schoolId();
    const stats$ = (id ? this.schools.getSchoolStats(id) : this.schools.getMyStats()).pipe(catchError(() => of(null)));
    const statuses$ = this.safe(this.master.getGeneralStatusOptions());
    (id ? this.schools.getSchool(id) : this.schools.getMySchool())
      .pipe(
        switchMap((school) =>
          forkJoin({ chain: this.loadSavedLocationChain(school), stats: stats$, statuses: statuses$ }).pipe(map((x) => ({ school, ...x }))),
        ),
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ school, chain, stats, statuses }) => {
          this.school.set(school);
          this.stats.set(stats);
          this.statusName.set(statuses.find((o) => o.value === String(school.schoolStatusId ?? ''))?.label ?? '');
          // The saved chain is loaded first so the cascading selects pre-select correctly.
          this.stateOptions.set(chain.states);
          this.districtOptions.set(chain.districts);
          this.cityOptions.set(chain.cities);
          this.locationText.set(this.locationOf(school));

          this.form.patchValue(profileToFormValue(school), { emitEvent: false });
          this.setChildrenEnabled({ state: !!school.countryId, district: !!school.stateId, city: !!school.districtId });
          this.form.updateValueAndValidity({ emitEvent: false });
          this.form.disable({ emitEvent: false }); // opens read-only
        },
        error: () => this.loadFailed.set(true), // already toasted by the error interceptor
      });
  }

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
        switchMap((state) => (state ? this.safe(this.master.getDistrictOptions(+c.countryId.value, +state)) : of([] as SelectOption[]))),
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

  protected startEdit(): void {
    if (!this.canEdit()) return;
    const c = this.form.controls;
    this.editing.set(true);
    this.form.enable({ emitEvent: false });
    this.setChildrenEnabled({ state: !!c.countryId.value, district: !!c.stateId.value, city: !!c.districtId.value });
  }

  /** Discards unsaved edits by reloading the saved profile. */
  protected cancelEdit(): void {
    this.submitted.set(false);
    this.logoChange = NO_LOGO_CHANGE;
    this.logoVersion.update((v) => v + 1);
    this.load();
  }

  private setChildrenEnabled(enabled: { state: boolean; district: boolean; city: boolean }): void {
    if (!this.editing()) return; // the whole form is disabled while viewing
    const c = this.form.controls;
    const apply = (ctrl: AbstractControl, on: boolean) => (on ? ctrl.enable({ emitEvent: false }) : ctrl.disable({ emitEvent: false }));
    apply(c.stateId, enabled.state);
    apply(c.districtId, enabled.district);
    apply(c.cityId, enabled.city);
  }

  // ── Template helpers ────────────────────────────────────────────────
  protected err(name: keyof ProfileForm['controls']): string | undefined {
    return firstErrorMessage(this.form.controls[name], this.submitted());
  }

  protected onLogoChanged(change: LogoChange): void {
    this.logoChange = change;
  }

  // ── Submit ──────────────────────────────────────────────────────────
  protected submit(): void {
    if (this.saving() || !this.editing() || !this.canEdit()) return;

    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.toast.error('Please fix the highlighted fields.');
      requestAnimationFrame(() => this.host.nativeElement.querySelector('.control.invalid')?.scrollIntoView({ block: 'center', behavior: 'smooth' }));
      return;
    }

    this.saving.set(true);
    this.save().pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe();
  }

  /** Save the details, then apply the pending logo change (new file or removal). A logo failure only warns. */
  private save(): Observable<unknown> {
    const { file, remove } = this.logoChange;
    return this.schools.updateMySchool(toProfileRequest(this.form)).pipe(
      switchMap((updated) => {
        const logo$ = remove ? this.schools.removeMyLogo() : file ? this.schools.uploadMyLogo(file) : null;
        return logo$
          ? logo$.pipe(
              catchError((e) => {
                this.toast.warning(this.messageOf(e), 'Profile saved, but the logo change failed');
                return of(updated);
              }),
            )
          : of(updated);
      }),
      tap((school) => {
        this.school.set(school);
        this.locationText.set(this.locationOf(school));
        this.logoChange = NO_LOGO_CHANGE;
        this.logoVersion.update((v) => v + 1);
        this.submitted.set(false);
        this.form.markAsPristine();
        this.editing.set(false);
        this.form.disable({ emitEvent: false });
        // Keep the topbar in step with what was just saved
        this.auth.patchUser({ schoolName: school.schoolName, logoUrl: school.logoUrl ?? null });
        this.toast.success('School profile updated successfully.');
      }),
      catchError(() => of(null)), // the error interceptor already toasted; keep the form for another try
    );
  }

  /** "City, State" from the loaded option lists (they match the saved ids at the moments this is called). */
  private locationOf(s: SchoolsListModel): string {
    const label = (opts: SelectOption[], id?: number | null) => opts.find((o) => o.value === String(id ?? ''))?.label;
    return [label(this.cityOptions(), s.cityId), label(this.stateOptions(), s.stateId)].filter(Boolean).join(', ');
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
