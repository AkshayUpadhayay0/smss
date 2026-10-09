import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, ReactiveFormsModule } from '@angular/forms';
import { Observable, catchError, finalize, forkJoin, map, of, switchMap, tap } from 'rxjs';
import { AuthService } from '../../../../core/services/auth.service';
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
import { LogoChange, LogoUploadComponent } from '../../../schools/components/logo-upload/logo-upload.component';
import { SchoolsListModel } from '../../../schools/models/school.model';
import { SchoolService } from '../../../schools/services/school.service';
import { firstErrorMessage } from '../../../schools/utils/school-validators';
import { ProfileForm, createProfileForm, profileToFormValue, toProfileRequest } from '../../utils/profile-form';

const NO_LOGO_CHANGE: LogoChange = { file: null, remove: false };

/** A School Admin's own school. The school is resolved server-side from the token; no id is ever in the URL or request. */
@Component({
  selector: 'app-school-profile',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, SelectComponent, LogoUploadComponent],
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

  protected readonly breadcrumbs: Breadcrumb[] = [{ label: 'School Profile' }];
  protected readonly form = createProfileForm();

  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  /** False = read-only view of the saved profile; true = fields editable. */
  protected readonly editing = signal(false);
  protected readonly submitted = signal(false);
  protected readonly school = signal<SchoolsListModel | null>(null);
  /** Bumped after each save to re-create the logo picker (clears its pending file). */
  protected readonly logoVersion = signal(0);
  private logoChange: LogoChange = NO_LOGO_CHANGE;

  protected readonly logoUrl = computed(() => this.schools.toLogoUrl(this.school()?.logoUrl));

  // ── Lookup options ──────────────────────────────────────────────────
  protected readonly schoolTypeOptions = toSignal(this.safe(this.master.getSchoolTypeOptions()), { initialValue: [] });
  protected readonly schoolLevelOptions = toSignal(this.safe(this.master.getSchoolLevelOptions()), { initialValue: [] });
  protected readonly boardTypeOptions = toSignal(this.safe(this.master.getBoardTypeOptions()), { initialValue: [] });
  protected readonly countryOptions = toSignal(this.safe(this.master.getCountryOptions()), { initialValue: [] });
  protected readonly stateOptions = signal<SelectOption[]>([]);
  protected readonly districtOptions = signal<SelectOption[]>([]);
  protected readonly cityOptions = signal<SelectOption[]>([]);

  ngOnInit(): void {
    this.setupLocationCascade();
    this.setChildrenEnabled({ state: false, district: false, city: false });
    this.load();
  }

  protected load(): void {
    this.editing.set(false);
    this.loading.set(true);
    this.loadFailed.set(false);
    this.schools
      .getMySchool()
      .pipe(
        switchMap((school) => this.loadSavedLocationChain(school).pipe(map((chain) => ({ school, chain })))),
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ school, chain }) => {
          this.school.set(school);
          // The saved chain is loaded first so the cascading selects pre-select correctly.
          this.stateOptions.set(chain.states);
          this.districtOptions.set(chain.districts);
          this.cityOptions.set(chain.cities);

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
    if (this.saving() || !this.editing()) return;

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

  private messageOf(e: unknown): string {
    const err = e as { error?: { message?: string }; message?: string };
    return err?.error?.message ?? err?.message ?? 'Something went wrong.';
  }

  /** A failed lookup shouldn't break the form: the field just stays empty (the interceptor already toasted). */
  private safe<T>(source: Observable<T[]>): Observable<T[]> {
    return source.pipe(catchError(() => of([] as T[])));
  }
}
