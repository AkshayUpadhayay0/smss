import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../icon/icon.component';
import { IconName } from '../icon/icons';

export interface Breadcrumb {
  label: string;
  /** Omit for the current (last) page. */
  link?: string;
}

/** Action buttons are content-projected: `<app-page-header ...><app-button>…</app-button></app-page-header>` */
@Component({
  selector: 'app-page-header',
  imports: [RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './page-header.component.scss',
  template: `
    <div class="header">
      <div class="heading">
        @if (icon()) {
          <span class="icon-box"><app-icon [name]="icon()!" [size]="22" /></span>
        }
        <div class="text">
          <h1>{{ title() }}</h1>
          @if (subtitle()) {
            <p class="subtitle">{{ subtitle() }}</p>
          }
        </div>
      </div>
      <div class="actions"><ng-content /></div>
    </div>

    @if (breadcrumbs().length) {
      <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a routerLink="/"><app-icon name="home" [size]="14" />Home</a>
        @for (crumb of breadcrumbs(); track crumb.label) {
          <app-icon name="chevron-right" [size]="14" />
          @if (crumb.link) {
            <a [routerLink]="crumb.link">{{ crumb.label }}</a>
          } @else {
            <span aria-current="page">{{ crumb.label }}</span>
          }
        }
      </nav>
    }
  `,
})
export class PageHeaderComponent {
  readonly title = input.required<string>();
  readonly subtitle = input<string>();
  readonly icon = input<IconName>();
  readonly breadcrumbs = input<Breadcrumb[]>([]);
}
