import { ChangeDetectionStrategy, Component, input, inject } from '@angular/core';
import { NgClass } from '@angular/common';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';

// A compact, self-drawn line-icon set (24x24, stroke-based) so the theme has
// zero dependency on an external icon library/font. Extend ICONS as needed.
const ICONS: Record<string, string> = {
  dashboard: '<rect x="3" y="3" width="7" height="9" rx="1.5"/><rect x="14" y="3" width="7" height="5" rx="1.5"/><rect x="14" y="12" width="7" height="9" rx="1.5"/><rect x="3" y="16" width="7" height="5" rx="1.5"/>',
  users: '<circle cx="9" cy="8" r="3.2"/><path d="M2.5 20c0-3.6 2.9-6 6.5-6s6.5 2.4 6.5 6"/><circle cx="17" cy="8.5" r="2.6"/><path d="M15.5 14.2c2.9.3 5 2.4 5 5.8"/>',
  user: '<circle cx="12" cy="8" r="3.6"/><path d="M4.5 20.5c0-4 3.4-6.8 7.5-6.8s7.5 2.8 7.5 6.8"/>',
  'user-plus': '<circle cx="9" cy="8" r="3.4"/><path d="M2.5 20c0-3.6 2.9-6 6.5-6s6.5 2.4 6.5 6"/><path d="M19 8v6M16 11h6"/>',
  'user-check': '<circle cx="9" cy="8" r="3.4"/><path d="M2.5 20c0-3.6 2.9-6 6.5-6s6.5 2.4 6.5 6"/><path d="M16 12.5l2 2 4-4.2"/>',
  'graduation-cap': '<path d="M2 9l10-5 10 5-10 5-10-5z"/><path d="M6 11.5V17c0 1.4 2.7 2.6 6 2.6s6-1.2 6-2.6v-5.5"/><path d="M22 9v6"/>',
  layers: '<path d="M12 2.5l9.5 5-9.5 5-9.5-5 9.5-5z"/><path d="M2.5 12l9.5 5 9.5-5"/><path d="M2.5 16.5l9.5 5 9.5-5"/>',
  grid: '<rect x="3" y="3" width="8" height="8" rx="1.5"/><rect x="13" y="3" width="8" height="8" rx="1.5"/><rect x="3" y="13" width="8" height="8" rx="1.5"/><rect x="13" y="13" width="8" height="8" rx="1.5"/>',
  calendar: '<rect x="3" y="4.5" width="18" height="16" rx="2"/><path d="M3 9.5h18M8 2.5v4M16 2.5v4"/>',
  clock: '<circle cx="12" cy="12" r="9.2"/><path d="M12 7v5l3.5 2"/>',
  'clipboard-list': '<rect x="5" y="4" width="14" height="17" rx="2"/><rect x="8.5" y="2" width="7" height="4" rx="1"/><path d="M8.5 11h7M8.5 15h7M8.5 19h4"/>',
  'check-square': '<rect x="3.5" y="3.5" width="17" height="17" rx="2.5"/><path d="M8 12l2.7 2.7L16.5 9"/>',
  'file-text': '<path d="M6 2.5h8l5 5V21a1 1 0 01-1 1H6a1 1 0 01-1-1V3.5a1 1 0 011-1z"/><path d="M14 2.5V8h5M8 13h8M8 17h8"/>',
  award: '<circle cx="12" cy="8.5" r="5.5"/><path d="M8.2 13.2L6 21l6-3 6 3-2.2-7.8"/>',
  bell: '<path d="M6 9a6 6 0 0112 0c0 4 1.5 5.5 1.5 5.5H4.5S6 13 6 9z"/><path d="M10 19a2 2 0 004 0"/>',
  mail: '<rect x="2.5" y="4.5" width="19" height="15" rx="2"/><path d="M3 6l9 6.5L21 6"/>',
  'message-square': '<path d="M3.5 5.5a2 2 0 012-2h13a2 2 0 012 2v9a2 2 0 01-2 2H9l-5 4v-4H5.5a2 2 0 01-2-2v-9z"/>',
  megaphone: '<path d="M3 10v4a1.5 1.5 0 001.5 1.5H6l5 4V4.5l-5 4H4.5A1.5 1.5 0 003 10z"/><path d="M13 8.3a5 5 0 010 7.4M17 5.5a9 9 0 010 13"/>',
  inbox: '<path d="M3 12h5l1.6 3h4.8l1.6-3h5"/><rect x="3" y="5" width="18" height="14" rx="2"/>',
  'log-in': '<path d="M12.5 3.5H19a1 1 0 011 1v15a1 1 0 01-1 1h-6.5M8 8l-4.5 4L8 16M3.5 12H15"/>',
  'log-out': '<path d="M11.5 3.5H5a1 1 0 00-1 1v15a1 1 0 001 1h6.5M16 8l4.5 4-4.5 4M20.5 12H9"/>',
  wallet: '<rect x="2.5" y="6" width="19" height="14" rx="2.5"/><path d="M2.5 10h19M16.5 15h2"/><path d="M6 6V5a2 2 0 012-2h9a2 2 0 012 2v1"/>',
  'credit-card': '<rect x="2.5" y="5" width="19" height="14" rx="2.5"/><path d="M2.5 10h19M6 15h4"/>',
  truck: '<rect x="1.5" y="7" width="13" height="10" rx="1.5"/><path d="M14.5 10.5H18l3 3.2V17h-2.5"/><circle cx="6" cy="18.5" r="1.8"/><circle cx="17" cy="18.5" r="1.8"/>',
  home: '<path d="M4 11l8-7 8 7"/><path d="M6 9.5V20a1 1 0 001 1h4v-6h2v6h4a1 1 0 001-1V9.5"/>',
  building: '<rect x="4" y="3" width="10" height="18" rx="1"/><rect x="14" y="9" width="6" height="12" rx="1"/><path d="M7 7h1M7 11h1M7 15h1M10.5 7h1M10.5 11h1M10.5 15h1"/>',
  bed: '<path d="M2.5 19V8a1.5 1.5 0 011.5-1.5h6A1.5 1.5 0 0111.5 8v4"/><path d="M2.5 12h19v7"/><path d="M11.5 12h10v-1a3 3 0 00-3-3h-4a3 3 0 00-3 3z"/><path d="M2.5 16.5h19"/>',
  book: '<path d="M4 4.5A1.5 1.5 0 015.5 3H12v18H5.5A1.5 1.5 0 014 19.5v-15z"/><path d="M20 4.5A1.5 1.5 0 0018.5 3H12v18h6.5a1.5 1.5 0 001.5-1.5v-15z"/>',
  'book-open': '<path d="M12 6.5C10.3 5 7.6 4.5 4.5 4.5v13.8c3.1 0 5.8.5 7.5 2 1.7-1.5 4.4-2 7.5-2V4.5c-3.1 0-5.8.5-7.5 2z"/><path d="M12 6.5v13.8"/>',
  'bar-chart': '<path d="M4 21V10M11 21V4M18 21v-7"/><path d="M2.5 21h19"/>',
  'pie-chart': '<path d="M12 2.5v9.5h9.5A9.5 9.5 0 1012 2.5z"/>',
  activity: '<path d="M2.5 12.5h4.5l2.3-6.3 4 13.6 2.4-7.3h6.3"/>',
  search: '<circle cx="10.8" cy="10.8" r="7.3"/><path d="M20.5 20.5l-4.8-4.8"/>',
  settings: '<circle cx="12" cy="12" r="3.2"/><path d="M19.4 13.5a1.7 1.7 0 00.3 1.9l.1.1a2 2 0 11-2.9 2.9l-.1-.1a1.7 1.7 0 00-1.9-.3 1.7 1.7 0 00-1 1.6v.2a2 2 0 11-4.1 0v-.1a1.7 1.7 0 00-1.1-1.6 1.7 1.7 0 00-1.9.3l-.1.1a2 2 0 11-2.9-2.9l.1-.1a1.7 1.7 0 00.3-1.9 1.7 1.7 0 00-1.6-1h-.2a2 2 0 110-4.1h.1A1.7 1.7 0 004.5 9.9a1.7 1.7 0 00-.3-1.9l-.1-.1a2 2 0 112.9-2.9l.1.1a1.7 1.7 0 001.9.3H9a1.7 1.7 0 001-1.6v-.2a2 2 0 114.1 0v.1a1.7 1.7 0 001 1.6 1.7 1.7 0 001.9-.3l.1-.1a2 2 0 112.9 2.9l-.1.1a1.7 1.7 0 00-.3 1.9V10a1.7 1.7 0 001.6 1h.2a2 2 0 110 4.1h-.1a1.7 1.7 0 00-1.6 1z"/>',
  sliders: '<path d="M4 21V14M4 10V3M12 21v-9M12 8V3M20 21v-6M20 11V3"/><path d="M1.5 14h5M9.5 8h5M17.5 11h5"/>',
  palette: '<path d="M12 2.5a9.5 9.5 0 100 19c1 0 1.8-.8 1.8-1.8 0-.5-.2-.9-.5-1.2-.3-.3-.5-.7-.5-1.2 0-1 .8-1.8 1.8-1.8h2.1a4.3 4.3 0 004.3-4.3c0-5-4.5-8.7-9-8.7z"/><circle cx="7" cy="10.5" r="1.1"/><circle cx="10.5" cy="6.8" r="1.1"/><circle cx="15" cy="7.3" r="1.1"/><circle cx="17.3" cy="11.5" r="1.1"/>',
  moon: '<path d="M20.5 14.8A9 9 0 119.2 3.5a7 7 0 0011.3 11.3z"/>',
  sun: '<circle cx="12" cy="12" r="4.5"/><path d="M12 2.5v2.3M12 19.2v2.3M4.6 4.6l1.6 1.6M17.8 17.8l1.6 1.6M2.5 12h2.3M19.2 12h2.3M4.6 19.4l1.6-1.6M17.8 6.2l1.6-1.6"/>',
  monitor: '<rect x="2.5" y="4" width="19" height="13" rx="2"/><path d="M8 21h8M12 17v4"/>',
  menu: '<path d="M3.5 6.5h17M3.5 12h17M3.5 17.5h17"/>',
  x: '<path d="M5 5l14 14M19 5L5 19"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  edit: '<path d="M14.5 4.5l5 5L8 21H3v-5z"/>',
  trash: '<path d="M4 7h16M9 7V4.5A1.5 1.5 0 0110.5 3h3A1.5 1.5 0 0115 4.5V7M18.5 7l-.8 12.5A2 2 0 0115.7 21H8.3a2 2 0 01-2-1.9L5.5 7"/>',
  eye: '<path d="M2 12s3.8-7 10-7 10 7 10 7-3.8 7-10 7-10-7-10-7z"/><circle cx="12" cy="12" r="3"/>',
  filter: '<path d="M3 4.5h18l-7 8.5V19l-4 2v-8L3 4.5z"/>',
  download: '<path d="M12 3.5v12M8 12l4 4 4-4"/><path d="M4 19.5h16"/>',
  upload: '<path d="M12 20.5v-12M8 12l4-4 4 4"/><path d="M4 19.5h16"/>',
  check: '<path d="M4.5 12.5l5 5 10-11"/>',
  'check-circle': '<circle cx="12" cy="12" r="9.2"/><path d="M8 12.3l2.7 2.7L16.5 9"/>',
  'x-circle': '<circle cx="12" cy="12" r="9.2"/><path d="M9 9l6 6M15 9l-6 6"/>',
  'alert-triangle': '<path d="M12 3.5l10 17H2l10-17z"/><path d="M12 10v4.2M12 17.5h.01"/>',
  'alert-circle': '<circle cx="12" cy="12" r="9.2"/><path d="M12 7.5v5.5M12 16.5h.01"/>',
  info: '<circle cx="12" cy="12" r="9.2"/><path d="M12 11v6M12 7.5h.01"/>',
  'arrow-up': '<path d="M12 19V5M5 12l7-7 7 7"/>',
  'arrow-down': '<path d="M12 5v14M19 12l-7 7-7-7"/>',
  'arrow-left': '<path d="M19 12H5M12 19l-7-7 7-7"/>',
  'arrow-right': '<path d="M5 12h14M12 5l7 7-7 7"/>',
  'chevron-down': '<path d="M5.5 8.5l6.5 6.5 6.5-6.5"/>',
  'chevron-right': '<path d="M8.5 5.5l6.5 6.5-6.5 6.5"/>',
  'chevron-left': '<path d="M15.5 5.5L9 12l6.5 6.5"/>',
  'chevrons-left': '<path d="M13 5.5L6.5 12l6.5 6.5M19.5 5.5L13 12l6.5 6.5"/>',
  star: '<path d="M12 2.8l3 6.2 6.7.9-4.9 4.7 1.2 6.7-6-3.3-6 3.3 1.2-6.7-4.9-4.7 6.7-.9z"/>',
  phone: '<path d="M5.3 3.5h3.4l1.3 4.6-2.3 1.8a13 13 0 006.3 6.3l1.8-2.3 4.6 1.3v3.4a1.5 1.5 0 01-1.6 1.5A17.5 17.5 0 013.8 5.1a1.5 1.5 0 011.5-1.6z"/>',
  'map-pin': '<path d="M12 21.5s7-6.3 7-12a7 7 0 10-14 0c0 5.7 7 12 7 12z"/><circle cx="12" cy="9.5" r="2.4"/>',
  printer: '<path d="M6.5 8.5V3.5h11v5"/><rect x="3.5" y="8.5" width="17" height="8" rx="2"/><path d="M6.5 15.5h11v6h-11z"/>',
  paperclip: '<path d="M20 11.5l-8.5 8.5a4 4 0 01-5.7-5.7l9-9a2.7 2.7 0 013.8 3.8l-8.7 8.7a1.3 1.3 0 01-1.9-1.9l7.9-7.9"/>',
  image: '<rect x="3" y="4" width="18" height="16" rx="2"/><circle cx="8.5" cy="9.5" r="1.6"/><path d="M21 16l-5.5-5.5L4 21"/>',
  'more-vertical': '<circle cx="12" cy="5" r="1.3"/><circle cx="12" cy="12" r="1.3"/><circle cx="12" cy="19" r="1.3"/>',
  'more-horizontal': '<circle cx="5" cy="12" r="1.3"/><circle cx="12" cy="12" r="1.3"/><circle cx="19" cy="12" r="1.3"/>',
  list: '<path d="M8.5 6.5h12M8.5 12h12M8.5 17.5h12"/><circle cx="4" cy="6.5" r="1"/><circle cx="4" cy="12" r="1"/><circle cx="4" cy="17.5" r="1"/>',
  lock: '<rect x="4.5" y="10.5" width="15" height="10" rx="2"/><path d="M8 10.5V7a4 4 0 018 0v3.5"/>',
  bus: '<rect x="3" y="4" width="18" height="12" rx="2"/><path d="M3 11.5h18M7 16v2.5M17 16v2.5"/><circle cx="7.5" cy="19" r="1.3"/><circle cx="16.5" cy="19" r="1.3"/>',
  briefcase: '<rect x="2.5" y="7" width="19" height="13" rx="2"/><path d="M8 7V5.5A1.5 1.5 0 019.5 4h5A1.5 1.5 0 0116 5.5V7"/><path d="M2.5 12.5h19"/>',
  send: '<path d="M22 2L11 13M22 2l-7 20-4-9-9-4z"/>',
  smartphone: '<rect x="6" y="2.5" width="12" height="19" rx="2"/><path d="M11 18.5h2"/>',
  globe: '<circle cx="12" cy="12" r="9.2"/><path d="M2.8 12h18.4M12 2.8a14 14 0 010 18.4M12 2.8a14 14 0 000 18.4"/>',
  'thumbs-up': '<path d="M7 11v9H4a1 1 0 01-1-1v-7a1 1 0 011-1h3zM7 11l3.5-7.5a2 2 0 012 0l.3.2a2 2 0 01.9 2.1L13 9h5.5a2 2 0 012 2.3l-1.4 7A2 2 0 0117.1 20H10a3 3 0 01-3-3v-6z"/>',
  shield: '<path d="M12 2.5l8 3v6c0 5-3.4 8.7-8 10-4.6-1.3-8-5-8-10v-6z"/>',
  zap: '<path d="M13 2.5L4 14h6l-1 7.5 9-11.5h-6z"/>',
  'refresh-cw': '<path d="M21 12a9 9 0 01-15.5 6.3L3 16M3 12a9 9 0 0115.5-6.3L21 8"/><path d="M3 16v-4h4M21 8v4h-4"/>',
  copy: '<rect x="8.5" y="8.5" width="12" height="12" rx="2"/><path d="M5.5 15.5h-1a2 2 0 01-2-2v-9a2 2 0 012-2h9a2 2 0 012 2v1"/>',
  play: '<path d="M6.5 4.5l13 7.5-13 7.5z"/>',
  pause: '<rect x="6.5" y="4.5" width="4" height="15" rx="1"/><rect x="13.5" y="4.5" width="4" height="15" rx="1"/>',
  volume: '<path d="M4 9.5v5h4l5 4v-13l-5 4z"/><path d="M17 8.5a5 5 0 010 7"/>',
  'trending-up': '<path d="M3 17l6-6 4 4 8-8"/><path d="M15 7h6v6"/>',
  'trending-down': '<path d="M3 7l6 6 4-4 8 8"/><path d="M15 17h6v-6"/>',
  minus: '<path d="M5 12h14"/>',
  loader: '<path d="M12 2.5v4M12 17.5v4M4.9 4.9l2.8 2.8M16.3 16.3l2.8 2.8M2.5 12h4M17.5 12h4M4.9 19.1l2.8-2.8M16.3 7.7l2.8-2.8"/>',
  'help-circle': '<circle cx="12" cy="12" r="9.2"/><path d="M9.2 9.3a2.8 2.8 0 115.3 1.3c-.7 1.1-2.1 1.4-2.1 3M12 17h.01"/>',
  wifi: '<path d="M2 8.5a15.5 15.5 0 0120 0M5.5 12.3a10.5 10.5 0 0113 0M9 16.1a5.5 5.5 0 016 0"/><circle cx="12" cy="19.5" r="1"/>',
  droplet: '<path d="M12 2.5s7 8 7 12.5a7 7 0 11-14 0c0-4.5 7-12.5 7-12.5z"/>',
  gift: '<rect x="3" y="8.5" width="18" height="12" rx="1.5"/><path d="M3 12.5h18M12 8.5v12"/><path d="M12 8.5c-1.8 0-4-1-4-3a2.5 2.5 0 014.9-.7c.4 1-.9 3.7-.9 3.7zM12 8.5c1.8 0 4-1 4-3a2.5 2.5 0 00-4.9-.7c-.4 1 .9 3.7.9 3.7z"/>',
};

@Component({
  selector: 'app-icon',
  standalone: true,
  imports: [NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg
      [attr.width]="size()"
      [attr.height]="size()"
      viewBox="0 0 24 24"
      fill="none"
      [attr.stroke]="color() || 'currentColor'"
      [attr.stroke-width]="strokeWidth()"
      stroke-linecap="round"
      stroke-linejoin="round"
      [ngClass]="klass()"
      [innerHTML]="path()"
    ></svg>
  `,
  styles: [`:host { display: inline-flex; line-height: 0; }`],
})
export class IconComponent {
  private sanitizer = inject(DomSanitizer);

  name = input.required<string>();
  size = input<number>(20);
  strokeWidth = input<number>(1.9);
  color = input<string>('');
  klass = input<string>('', { alias: 'class' });

  path(): SafeHtml {
    const svg = ICONS[this.name()] ?? ICONS['help-circle'];
    return this.sanitizer.bypassSecurityTrustHtml(svg);
  }
}
