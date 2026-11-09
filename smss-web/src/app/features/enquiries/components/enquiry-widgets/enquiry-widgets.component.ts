import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { EnquiryService } from '../../services/enquiry.service';

/** Four numbers about the public enquiries, for the top of the Super Admin dashboard. */
@Component({
  selector: 'app-enquiry-widgets',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './enquiry-widgets.component.scss',
  template: `
    <section class="tiles" aria-label="Enquiries from the website">
      @for (t of tiles(); track t.label) {
        <a class="tile" [routerLink]="'/enquiries'" [queryParams]="t.query">
          <span class="label">{{ t.label }}</span>
          <strong>{{ t.value }}</strong>
          <small>{{ t.note }}</small>
        </a>
      }
    </section>
  `,
})
export class EnquiryWidgetsComponent implements OnInit {
  private readonly service = inject(EnquiryService);

  protected readonly tiles = computed(() => {
    const s = this.service.summary();
    const n = (v: number | undefined) => (v === undefined ? '–' : String(v));
    return [
      { label: 'New enquiries', value: n(s?.new), note: 'Waiting for a reply', query: { status: 'NEW' } },
      { label: 'Demo requests', value: n(s?.newDemo), note: 'New, from Request a Demo', query: { type: 'DEMO', status: 'NEW' } },
      { label: 'Contact messages', value: n(s?.newContact), note: 'New, from Contact Us', query: { type: 'CONTACT', status: 'NEW' } },
      { label: 'This month', value: n(s?.totalThisMonth), note: 'All enquiries received', query: {} },
    ];
  });

  ngOnInit(): void {
    this.service.refreshSummary();
  }
}
