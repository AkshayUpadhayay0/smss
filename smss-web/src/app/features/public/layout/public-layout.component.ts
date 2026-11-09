import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { SITE_CONFIG } from '../../../core/config/site.config';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { LogoComponent } from '../../../shared/components/logo/logo.component';
import { PUBLIC_NAV } from '../config/landing.content';

/** Header + footer around every public page. Separate from the signed-in app's layout. */
@Component({
  selector: 'app-public-layout',
  imports: [RouterOutlet, RouterLink, IconComponent, LogoComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './public-layout.component.scss',
  template: `
    <a class="skip" href="#main">Skip to main content</a>

    <header class="header">
      <div class="pub-container bar">
        <a class="brand" routerLink="/" aria-label="SMSS home"><app-logo /></a>

        <nav class="nav" aria-label="Main navigation" [class.open]="menuOpen()" id="primary-nav">
          @for (link of nav; track link.label) {
            <a [routerLink]="link.route" [fragment]="link.fragment" (click)="menuOpen.set(false)">{{ link.label }}</a>
          }
          <div class="actions">
            <a class="pub-btn pub-btn-secondary pub-btn-sm" routerLink="/login" (click)="menuOpen.set(false)">Sign in</a>
            <a class="pub-btn pub-btn-primary pub-btn-sm" routerLink="/request-demo" (click)="menuOpen.set(false)">Request a Demo</a>
          </div>
        </nav>

        <button type="button" class="burger" aria-controls="primary-nav" [attr.aria-expanded]="menuOpen()" (click)="menuOpen.set(!menuOpen())">
          <app-icon [name]="menuOpen() ? 'x' : 'menu'" [size]="24" />
          <span class="sr-only">{{ menuOpen() ? 'Close menu' : 'Open menu' }}</span>
        </button>
      </div>
    </header>

    <main id="main" tabindex="-1">
      <router-outlet />
    </main>

    <footer class="footer">
      <div class="pub-container grid">
        <div class="about">
          <app-logo />
          <p>{{ site.description }}</p>
        </div>

        <nav aria-label="Footer navigation">
          <h2>Explore</h2>
          <ul>
            @for (link of nav; track link.label) {
              <li><a [routerLink]="link.route" [fragment]="link.fragment">{{ link.label }}</a></li>
            }
            <li><a routerLink="/request-demo">Request a Demo</a></li>
            <li><a routerLink="/login">Sign in</a></li>
          </ul>
        </nav>

        <div>
          <h2>Contact</h2>
          <ul class="contact">
            <li><app-icon name="mail" [size]="16" /><a [href]="'mailto:' + site.email">{{ site.email }}</a></li>
            <li><app-icon name="phone" [size]="16" /><a [href]="'tel:' + site.phoneHref">{{ site.phone }}</a></li>
            <li><app-icon name="map-pin" [size]="16" /><span>{{ site.address }}</span></li>
          </ul>
        </div>

        <nav aria-label="Legal">
          <h2>Legal</h2>
          <ul>
            <li><a routerLink="/privacy-policy">Privacy Policy</a></li>
            <li><a routerLink="/terms">Terms of Use</a></li>
          </ul>
        </nav>
      </div>
      <div class="pub-container legal">
        <p>&copy; {{ site.year }} {{ site.legalName }}. All rights reserved.</p>
      </div>
    </footer>
  `,
})
export class PublicLayoutComponent {
  protected readonly site = SITE_CONFIG;
  protected readonly nav = PUBLIC_NAV;
  protected readonly menuOpen = signal(false);

  constructor() {
    // A page change closes the mobile menu
    inject(Router)
      .events.pipe(
        filter((e) => e instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.menuOpen.set(false));
  }
}
