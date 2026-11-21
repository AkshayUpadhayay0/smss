import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, afterNextRender, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MARQUEE_MIN_SCHOOLS, PartnerSchool, schoolHue, schoolInitials } from '../../models/partner-school.model';
import { PartnerSchoolService } from '../../services/partner-school.service';

type LoadState = 'idle' | 'loading' | 'ready' | 'hidden';

/**
 * "Our Partner Schools" on the landing page. Loads in the BROWSER only (afterNextRender never runs during the build-time prerender), so the static HTML
 * contains no school data and not even this section's heading. While loading it shows a skeleton of the same height; an empty list or any failure
 * removes the section without a trace.
 */
@Component({
  selector: 'app-partner-schools',
  imports: [NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './partner-schools.component.scss',
  template: `
    @switch (state()) {
      @case ('loading') {
        <section class="pub-section partners" aria-hidden="true">
          <div class="pub-container">
            <div class="sk sk-title"></div>
            <div class="sk sk-line"></div>
            <div class="grid">
              @for (n of skeleton; track n) {
                <div class="sk sk-card"></div>
              }
            </div>
          </div>
        </section>
      }
      @case ('ready') {
        <section id="partner-schools" class="pub-section partners" aria-labelledby="partners-title">
          <div class="pub-container">
            <h2 id="partners-title" class="pub-title">Our Partner Schools</h2>
            <p class="pub-lead">Schools that run on SMSS and have agreed to be listed here.</p>

            @if (marquee()) {
              <div class="marquee-bar">
                <button type="button" class="pause" (click)="paused.set(!paused())" [attr.aria-pressed]="paused()">
                  {{ paused() ? 'Play' : 'Pause' }} scrolling
                </button>
              </div>
              <div class="marquee" [class.paused]="paused()">
                <div class="track">
                  <ul class="row" aria-label="Partner schools">
                    @for (s of schools(); track s.name) {
                      <ng-container *ngTemplateOutlet="card; context: { $implicit: s }" />
                    }
                  </ul>
                  <!-- Second copy only to make the loop seamless: hidden from assistive technology -->
                  <ul class="row" aria-hidden="true">
                    @for (s of schools(); track s.name) {
                      <ng-container *ngTemplateOutlet="card; context: { $implicit: s, hidden: true }" />
                    }
                  </ul>
                </div>
              </div>
            } @else {
              <ul class="grid static" aria-label="Partner schools">
                @for (s of schools(); track s.name) {
                  <ng-container *ngTemplateOutlet="card; context: { $implicit: s }" />
                }
              </ul>
            }
          </div>
        </section>
      }
    }

    <ng-template #card let-s let-hidden="hidden">
      <li class="school">
        @if (s.logoUrl && !failed().has(s.name)) {
          <img
            class="logo"
            [src]="s.logoUrl"
            [alt]="hidden ? '' : s.name + ' logo'"
            width="56"
            height="56"
            loading="lazy"
            decoding="async"
            (error)="markFailed(s.name)"
          />
        } @else {
          <span class="logo initials" [style.--h]="hue(s.name)" aria-hidden="true">{{ initials(s.name) }}</span>
        }
        <span class="meta">
          <span class="name">{{ s.name }}</span>
          @if (s.city) {
            <span class="city">{{ s.city }}</span>
          }
        </span>
      </li>
    </ng-template>
  `,
})
export class PartnerSchoolsComponent {
  private readonly service = inject(PartnerSchoolService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly skeleton = [1, 2, 3, 4];
  protected readonly state = signal<LoadState>('idle');
  protected readonly schools = signal<PartnerSchool[]>([]);
  protected readonly failed = signal<ReadonlySet<string>>(new Set());
  protected readonly paused = signal(false);
  protected readonly marquee = computed(() => this.schools().length >= MARQUEE_MIN_SCHOOLS);

  constructor() {
    afterNextRender(() => this.load());
  }

  private load(): void {
    this.state.set('loading');
    this.service
      .getPartnerSchools()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          this.schools.set(list);
          this.state.set(list.length ? 'ready' : 'hidden');
        },
        error: () => this.state.set('hidden'), // silent: a missing section is better than an error on the home page
      });
  }

  protected initials = schoolInitials;
  protected hue = schoolHue;

  protected markFailed(name: string): void {
    this.failed.update((s) => new Set(s).add(name));
  }
}
