import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SITE_CONFIG } from '../../../core/config/site.config';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { EnquiryFormComponent } from '../components/enquiry-form/enquiry-form.component';
import { EnquiryType } from '../models/enquiry.model';

/**
 * /contact and /request-demo: the same page shell, told which form to show by the route's `data: { type }`
 * (bound to the input through withComponentInputBinding).
 */
@Component({
  selector: 'app-form-page',
  imports: [RouterLink, IconComponent, EnquiryFormComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './form-page.component.scss',
  template: `
    <section class="wrap">
      <div class="pub-container layout">
        <div class="intro">
          <span class="pub-eyebrow">{{ type() === 'DEMO' ? 'Request a Demo' : 'Contact us' }}</span>
          <h1>{{ type() === 'DEMO' ? 'See SMSS in action' : 'We would love to hear from you' }}</h1>
          <p class="pub-lead">
            {{
              type() === 'DEMO'
                ? 'Tell us about your school and when you are free. We will walk you through the modules that matter to you.'
                : 'Questions about SMSS, support or partnerships? Send a message and we will reply by email.'
            }}
          </p>
          <ul class="details">
            <li><app-icon name="mail" [size]="18" /><a [href]="'mailto:' + site.email">{{ site.email }}</a></li>
            <li><app-icon name="phone" [size]="18" /><a [href]="'tel:' + site.phoneHref">{{ site.phone }}</a></li>
            <li><app-icon name="map-pin" [size]="18" /><span>{{ site.address }}</span></li>
          </ul>
          <p class="switch">
            @if (type() === 'DEMO') {
              Just a question? <a class="pub-link" routerLink="/contact">Contact us</a>.
            } @else {
              Want to see the product? <a class="pub-link" routerLink="/request-demo">Request a demo</a>.
            }
          </p>
        </div>

        <div class="panel">
          <app-enquiry-form [type]="type()" />
        </div>
      </div>
    </section>
  `,
})
export class FormPageComponent {
  readonly type = input.required<EnquiryType>();
  protected readonly site = SITE_CONFIG;
}
