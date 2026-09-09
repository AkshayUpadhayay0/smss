import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { SidebarComponent } from '../sidebar/sidebar';
import { HeaderComponent } from '../header/header';
import { FooterComponent } from '../footer/footer';
import { ThemeService } from '../../core/services/theme.service';

@Component({
  selector: 'app-admin-layout',
  standalone: true,
  imports: [RouterOutlet, SidebarComponent, HeaderComponent, FooterComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="shell">
      <app-sidebar />
      <div class="main" [style.margin-left.px]="mainOffset()">
        <app-header />
        <main class="content">
          <router-outlet />
        </main>
        <app-footer />
      </div>
    </div>
  `,
  styles: [`
    .shell { min-height: 100vh; background: var(--background); }
    .main { display: flex; flex-direction: column; min-height: 100vh; transition: margin-left var(--transition-base); }
    .content { flex: 1; }
    @media (max-width: 1024px) { .main { margin-left: 0 !important; } }
  `],
})
export class AdminLayoutComponent {
  theme = inject(ThemeService);

  mainOffset(): number {
    if (typeof window !== 'undefined' && window.innerWidth <= 1024) return 0;
    const style = this.theme.sidebarStyle();
    return style === 'icon' ? 80 : 272;
  }
}
