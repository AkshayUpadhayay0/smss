import { ChangeDetectionStrategy, Component, HostListener, effect, inject, signal, untracked } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { OnboardingService } from '../../core/services/onboarding.service';
import { SetupProgressComponent } from '../../features/onboarding/components/setup-progress/setup-progress.component';
import { SidebarComponent } from './sidebar/sidebar.component';
import { TopbarComponent } from './topbar/topbar.component';

const MOBILE_QUERY = '(max-width: 991px)';

@Component({
  selector: 'app-main-layout',
  imports: [RouterOutlet, SidebarComponent, TopbarComponent, SetupProgressComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './main-layout.component.scss',
  template: `
    <div class="shell" [class.collapsed]="collapsed() && !isMobile()" [class.mobile]="isMobile()" [class.drawer-open]="drawerOpen()">
      <app-sidebar
        class="sidebar"
        [collapsed]="collapsed() && !isMobile()"
        (navigated)="drawerOpen.set(false)"
        (expandRequested)="collapsed.set(false)"
      />
      @if (isMobile() && drawerOpen()) {
        <div class="backdrop" (click)="drawerOpen.set(false)"></div>
      }
      <div class="main">
        <app-topbar class="topbar" (menuToggle)="toggleSidebar()" />
        <main class="content">
          <app-setup-progress />
          <router-outlet />
        </main>
      </div>
    </div>
  `,
})
export class MainLayoutComponent {
  private readonly onboarding = inject(OnboardingService);
  protected readonly isMobile = signal(window.matchMedia(MOBILE_QUERY).matches);
  protected readonly collapsed = signal(false);
  protected readonly drawerOpen = signal(false);

  constructor() {
    // Reads the setup status once per signed-in user (School Admin, Principal, Accountant) so the menu and the checklist know it
    effect(() => {
      if (this.onboarding.eligible()) untracked(() => this.onboarding.ensure().subscribe());
    });
  }

  @HostListener('window:resize')
  protected onResize(): void {
    const mobile = window.matchMedia(MOBILE_QUERY).matches;
    this.isMobile.set(mobile);
    if (!mobile) this.drawerOpen.set(false);
  }

  protected toggleSidebar(): void {
    if (this.isMobile()) this.drawerOpen.update((v) => !v);
    else this.collapsed.update((v) => !v);
  }
}
