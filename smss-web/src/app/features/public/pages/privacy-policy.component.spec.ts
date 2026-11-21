import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PRIVACY_LAST_UPDATED, SITE_CONFIG } from '../../../core/config/site.config';
import { PRIVACY_TOC, PrivacyPolicyComponent } from './privacy-policy.component';

describe('PrivacyPolicyComponent', () => {
  function render() {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const f = TestBed.createComponent(PrivacyPolicyComponent);
    f.detectChanges();
    return f.nativeElement as HTMLElement;
  }
  const flat = (el: HTMLElement) => (el.textContent ?? '').replace(/\s+/g, ' ');

  it('has one h1 and one h2 per section, each the target of a contents link', () => {
    const el = render();
    expect(el.querySelectorAll('h1').length).toBe(1);
    expect(el.querySelector('h1')!.textContent).toBe('Privacy Policy');
    const sections = [...el.querySelectorAll('section > h2')];
    expect(sections.length).toBe(PRIVACY_TOC.length);
    expect(sections.map((h) => h.id)).toEqual(PRIVACY_TOC.map((t) => t.id));
    expect(el.querySelectorAll('.toc a').length).toBe(PRIVACY_TOC.length);
    for (const h of sections) expect(h.getAttribute('tabindex')).toBe('-1');
  });

  it('reads as a finished policy: no placeholder text, no draft banner, no highlighted blanks', () => {
    const el = render();
    const text = flat(el);
    expect(text).not.toMatch(/\[TO CONFIRM/i);
    expect(text).not.toMatch(/Draft|pending legal review|to be confirmed|placeholder/i);
    expect(el.querySelector('.todo, .review')).toBeNull();
  });

  it('shows the last-updated date from the constant', () => {
    const el = render();
    expect(el.querySelector('time')!.getAttribute('datetime')).toBe(PRIVACY_LAST_UPDATED);
    expect(el.querySelector('time')!.textContent).toContain('21 November 2026');   // the date as written, in any time zone
  });

  it('takes the developer name and contact details from the config', () => {
    const text = flat(render());
    expect(text).toContain(SITE_CONFIG.legalName);
    expect(text).toContain(SITE_CONFIG.address);
    expect(text).toContain(SITE_CONFIG.email);
  });

  it('has the Driver live tracking section with the required meaning', () => {
    const el = render();
    const h = [...el.querySelectorAll('h2')].find((x) => x.textContent?.includes('Location permission: Driver live tracking'))!;
    expect(h).toBeTruthy();
    const body = flat(h.parentElement as HTMLElement);
    for (const needle of [
      'records the vehicle',
      'in the background',
      'even when the screen is off or another app is in use',
      'persistent notification',
      'stops when the driver ends it',
      'Only drivers use this feature',
      'School Admin and the Principal',
      'no location history',
      'not used for advertising',
      'not sold',
      "location of students, teachers, guardians or any other user is never collected",
    ])
      expect(body.toLowerCase()).toContain(needle.toLowerCase());
  });

  it('covers the website and the app, sharing, retention, rights and children', () => {
    const text = flat(render());
    for (const needle of [
      'the SMSS website',
      'the SMSS mobile app',
      'do not sell personal data',
      'only as long as needed for the purpose they were collected for',
      'deleted or anonymised on a valid request',
      'contact your school first',
      'we also handle requests sent to us',
      "Children's privacy",
      'Google Firebase Cloud Messaging',
      'OpenStreetMap',
      'Google Fonts',
    ])
      expect(text).toContain(needle);
  });
});
