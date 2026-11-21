import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { showsSetupProgress } from '../../../../core/config/setup-gate';
import { OnboardingService } from '../../../../core/services/onboarding.service';
import { IconComponent } from '../../../../shared/components/icon/icon.component';

/**
 * The setup checklist of a school that has not finished its first-login setup. A School Admin sees the steps (each one links to its screen)
 * on the dashboard and above the School Setup / Fee Setup screens; a Principal or Accountant only sees a short banner on the dashboard.
 * Renders nothing once the setup is complete (or while the status is unknown).
 */
@Component({
  selector: 'app-setup-progress',
  imports: [RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './setup-progress.component.scss',
  templateUrl: './setup-progress.component.html',
})
export class SetupProgressComponent {
  protected readonly onboarding = inject(OnboardingService);
  private readonly router = inject(Router);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      startWith(null),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected readonly onDashboard = computed(() => this.url().split(/[?#]/)[0] === '/dashboard');
  protected readonly visible = computed(() => this.onboarding.incomplete() && showsSetupProgress(this.url()));
  protected readonly steps = computed(() => this.onboarding.status()?.steps ?? []);
  protected readonly doneCount = computed(() => this.steps().filter((s) => s.done).length);
  protected readonly percent = computed(() => (this.steps().length ? Math.round((this.doneCount() / this.steps().length) * 100) : 0));
}
