import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { BrandingService } from '../../core/services/branding.service';

@Component({
  selector: 'app-footer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <footer class="app-footer">
      <span class="text-muted">© {{ year }} {{ branding.branding().name }}. All rights reserved.</span>
      <span class="text-muted">School Management System Theme v1.0</span>
    </footer>
  `,
  styles: [`
    .app-footer {
      display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 8px;
      padding: var(--space-5) var(--space-6); font-size: 12px; border-top: 1px solid var(--border);
    }
    @media (max-width: 640px) { .app-footer { flex-direction: column; text-align: center; padding: var(--space-4); } }
  `],
})
export class FooterComponent {
  branding = inject(BrandingService);
  year = new Date().getFullYear();
}
