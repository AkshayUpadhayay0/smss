import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { FEATURES, MODULES, STEPS } from '../../config/landing.content';

@Component({
  selector: 'app-landing',
  imports: [RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './landing.component.scss',
  template: `
    <section class="hero" aria-labelledby="hero-title">
      <div class="pub-container hero-grid">
        <div class="hero-copy">
          <span class="pub-eyebrow">School management, simplified</span>
          <h1 id="hero-title">Run your whole school from one secure platform</h1>
          <p class="pub-lead">
            Admissions, students, attendance, examinations, fees and notices for every school you manage, with the right access for every role.
          </p>
          <div class="cta">
            <a class="pub-btn pub-btn-primary" routerLink="/request-demo">Request a Demo</a>
            <a class="pub-btn pub-btn-secondary" routerLink="/login">Sign in</a>
          </div>
        </div>

        <!-- Decorative illustration: shapes only, no figures -->
        <div class="mock" aria-hidden="true">
          <div class="mock-bar"><i></i><i></i><i></i></div>
          <div class="mock-body">
            <div class="mock-side"><b></b><b></b><b></b><b></b></div>
            <div class="mock-main">
              <div class="tiles"><span></span><span></span><span></span></div>
              <div class="chart"><i style="height: 38%"></i><i style="height: 62%"></i><i style="height: 48%"></i><i style="height: 80%"></i><i style="height: 66%"></i><i style="height: 92%"></i></div>
              <div class="rows"><span></span><span></span><span></span></div>
            </div>
          </div>
        </div>
      </div>
    </section>

    <section id="features" class="pub-section" aria-labelledby="features-title">
      <div class="pub-container">
        <span class="pub-eyebrow">Why SMSS</span>
        <h2 id="features-title" class="pub-title">Built for schools, designed for scale</h2>
        <p class="pub-lead">Everything a school office, a classroom and a family needs, in one place that stays simple to use.</p>
        <ul class="cards">
          @for (f of features; track f.title) {
            <li class="card">
              <span class="icon"><app-icon [name]="f.icon" [size]="22" /></span>
              <h3>{{ f.title }}</h3>
              <p>{{ f.text }}</p>
            </li>
          }
        </ul>
      </div>
    </section>

    <section id="modules" class="pub-section alt" aria-labelledby="modules-title">
      <div class="pub-container">
        <span class="pub-eyebrow">Modules</span>
        <h2 id="modules-title" class="pub-title">One platform, every part of school life</h2>
        <p class="pub-lead">Start with what you need today. Modules work together, so nothing is entered twice.</p>
        <ul class="cards">
          @for (m of modules; track m.title) {
            <li class="card">
              <span class="icon"><app-icon [name]="m.icon" [size]="22" /></span>
              <h3>
                {{ m.title }}
                @if (m.comingSoon) {
                  <span class="soon">Coming soon</span>
                }
              </h3>
              <p>{{ m.text }}</p>
            </li>
          }
        </ul>
      </div>
    </section>

    <section id="how-it-works" class="pub-section" aria-labelledby="how-title">
      <div class="pub-container">
        <span class="pub-eyebrow">How it works</span>
        <h2 id="how-title" class="pub-title">From sign-up to your first day live</h2>
        <ol class="steps">
          @for (s of steps; track s.title; let i = $index) {
            <li>
              <span class="num" aria-hidden="true">{{ i + 1 }}</span>
              <h3>{{ s.title }}</h3>
              <p>{{ s.text }}</p>
            </li>
          }
        </ol>
      </div>
    </section>

    <section class="band" aria-labelledby="cta-title">
      <div class="pub-container band-inner">
        <div>
          <h2 id="cta-title">See SMSS working for your school</h2>
          <p>Book a short walkthrough, or send us your questions. We reply promptly.</p>
        </div>
        <div class="cta">
          <a class="pub-btn pub-btn-light" routerLink="/request-demo">Request a Demo</a>
          <a class="pub-btn pub-btn-outline-light" routerLink="/contact">Contact us</a>
        </div>
      </div>
    </section>
  `,
})
export class LandingComponent {
  protected readonly features = FEATURES;
  protected readonly modules = MODULES;
  protected readonly steps = STEPS;
}
