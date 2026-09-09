import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { BrandingService } from '../../core/services/branding.service';
import { IconComponent } from '../../shared/components/icon/icon';

@Component({
  selector: 'app-auth-layout',
  standalone: true,
  imports: [RouterOutlet, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="auth-shell">
      <div class="brand-panel">
        <div class="brand-panel-inner">
          <div class="logo"><span>{{ branding.branding().logoInitials }}</span></div>
          <h1>{{ branding.branding().name }}</h1>
          <p>{{ branding.branding().tagline }}</p>
          <ul class="feature-list">
            <li><app-icon name="check-circle" [size]="18" /> Complete academic management</li>
            <li><app-icon name="check-circle" [size]="18" /> Real-time attendance & fee tracking</li>
            <li><app-icon name="check-circle" [size]="18" /> Dedicated student & parent portal</li>
          </ul>
        </div>
      </div>
      <div class="form-panel">
        <router-outlet />
      </div>
    </div>
  `,
  styles: [`
    .auth-shell { min-height: 100vh; display: grid; grid-template-columns: 1fr 1fr; background: var(--background); }
    .brand-panel {
      background: linear-gradient(150deg, var(--primary), var(--primary-hover));
      display: flex; align-items: center; justify-content: center; padding: var(--space-10); color: #fff; position: relative; overflow: hidden;
    }
    .brand-panel::before, .brand-panel::after {
      content: ''; position: absolute; border-radius: 50%; background: rgba(255,255,255,0.08);
    }
    .brand-panel::before { width: 340px; height: 340px; top: -120px; right: -100px; }
    .brand-panel::after { width: 240px; height: 240px; bottom: -80px; left: -60px; }
    .brand-panel-inner { max-width: 420px; position: relative; z-index: 1; }
    .logo {
      width: 64px; height: 64px; border-radius: var(--radius-lg); background: rgba(255,255,255,0.18);
      display: flex; align-items: center; justify-content: center; font-size: 24px; font-weight: 800; font-family: var(--font-display); margin-bottom: var(--space-6);
    }
    .brand-panel-inner h1 { font-size: 28px; color: #fff; margin-bottom: var(--space-3); }
    .brand-panel-inner p { font-size: 15px; opacity: 0.9; margin-bottom: var(--space-8); }
    .feature-list { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: var(--space-4); }
    .feature-list li { display: flex; align-items: center; gap: 12px; font-size: 14px; opacity: 0.95; }

    .form-panel { display: flex; align-items: center; justify-content: center; padding: var(--space-6); }

    @media (max-width: 900px) { .auth-shell { grid-template-columns: 1fr; } .brand-panel { display: none; } }
  `],
})
export class AuthLayoutComponent {
  branding = inject(BrandingService);
}
