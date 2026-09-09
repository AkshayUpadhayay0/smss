import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../../shared/components/icon/icon';

@Component({
  selector: 'app-error-page',
  standalone: true,
  imports: [RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="error-wrap">
      <div class="error-card animate-in">
        <div class="code-badge" [style.background]="bg()" [style.color]="color()">{{ code() }}</div>
        <div class="icon-circle" [style.background]="bg()" [style.color]="color()">
          <app-icon [name]="icon()" [size]="40" [strokeWidth]="1.5" />
        </div>
        <h1>{{ title() }}</h1>
        <p class="text-secondary">{{ message() }}</p>
        <div class="actions">
          <a routerLink="/dashboard" class="btn btn-primary"><app-icon name="home" [size]="16" /> Back to Dashboard</a>
          <a routerLink="/dashboard" class="btn btn-outline" (click)="goBack()"><app-icon name="arrow-left" [size]="16" /> Go Back</a>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .error-wrap { min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: var(--space-6); background: var(--background); }
    .error-card { text-align: center; max-width: 460px; }
    .code-badge {
      display: inline-block; padding: 4px 14px; border-radius: var(--radius-full); font-weight: 800; font-size: 13px;
      font-family: var(--font-display); margin-bottom: var(--space-6);
    }
    .icon-circle {
      width: 96px; height: 96px; border-radius: var(--radius-full); display: flex; align-items: center; justify-content: center;
      margin: 0 auto var(--space-6);
    }
    h1 { font-size: 24px; margin-bottom: var(--space-3); }
    p { font-size: 14px; margin-bottom: var(--space-8); }
    .actions { display: flex; gap: var(--space-3); justify-content: center; flex-wrap: wrap; }
  `],
})
export class ErrorPageComponent {
  code = input('404');
  icon = input('alert-triangle');
  title = input('Page not found');
  message = input('The page you are looking for might have been removed or is temporarily unavailable.');
  color = input('var(--danger)');
  bg = input('var(--danger-light)');

  goBack(): void {
    if (typeof history !== 'undefined') history.back();
  }
}
