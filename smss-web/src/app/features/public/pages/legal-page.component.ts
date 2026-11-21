import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SITE_CONFIG } from '../../../core/config/site.config';

type LegalKind = 'terms';

const CONTENT: Record<LegalKind, { title: string; sections: { heading: string; text: string }[] }> = {
  terms: {
    title: 'Terms of Use',
    sections: [
      { heading: 'Using this website', text: 'This website describes the SMSS platform. Use it lawfully and do not attempt to disrupt it.' },
      { heading: 'Accounts', text: 'Accounts for the platform are created for registered schools. Keep your login details private.' },
      { heading: 'Changes', text: 'Placeholder: these terms will be updated and the date of the latest version shown here.' },
      { heading: 'Contact', text: 'Questions about these terms can be sent through the Contact page.' },
    ],
  },
};

/** Placeholder Terms page (the Privacy Policy has its own component). The wording must be reviewed and replaced before launch. */
@Component({
  selector: 'app-legal-page',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './legal-page.component.scss',
  template: `
    <article class="pub-container doc">
      <p class="review" role="note"><strong>Content to be reviewed.</strong> This is placeholder text, not a final legal document.</p>
      <h1>{{ content().title }}</h1>
      @for (s of content().sections; track s.heading) {
        <section>
          <h2>{{ s.heading }}</h2>
          <p>{{ s.text }}</p>
        </section>
      }
      <p class="back">Questions? <a class="pub-link" routerLink="/contact">Contact {{ site.name }}</a>.</p>
    </article>
  `,
})
export class LegalPageComponent {
  readonly kind = input<LegalKind>('terms');
  protected readonly site = SITE_CONFIG;
  protected content() {
    return CONTENT[this.kind()];
  }
}
