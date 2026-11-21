import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { PartnerSchool, schoolInitials } from '../../models/partner-school.model';
import { PartnerSchoolsComponent } from './partner-schools.component';

const URL = `${environment.apiUrl}/api/public/partner-schools`;
const school = (name: string, over: Partial<PartnerSchool> = {}): PartnerSchool => ({ name, logoUrl: null, city: 'Dehradun', state: 'Uttarakhand', ...over });

describe('PartnerSchoolsComponent', () => {
  let http: HttpTestingController;

  function open(): ComponentFixture<PartnerSchoolsComponent> {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(PartnerSchoolsComponent);
    f.detectChanges(); // creates the view; afterNextRender then starts the request
    TestBed.tick();
    f.detectChanges();
    return f;
  }
  const el = (f: ComponentFixture<unknown>) => f.nativeElement as HTMLElement;
  afterEach(() => http.verify());

  it('shows a skeleton (and no heading) while loading', () => {
    const f = open();
    expect(el(f).querySelector('h2')).toBeNull();
    expect(el(f).querySelector('.sk')).not.toBeNull();
    http.expectOne(URL).flush({ status: true, statusCode: 200, message: 'ok', data: [] });
  });

  it('is removed entirely when the list is empty', () => {
    const f = open();
    http.expectOne(URL).flush({ status: true, statusCode: 200, message: 'ok', data: [] });
    f.detectChanges();
    expect(el(f).querySelector('section')).toBeNull();
    expect(el(f).textContent?.trim()).toBe('');
  });

  it('is removed entirely when the request fails', () => {
    const f = open();
    http.expectOne(URL).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    f.detectChanges();
    expect(el(f).querySelector('section')).toBeNull();
    expect(el(f).querySelector('h2')).toBeNull();
  });

  it('shows one card per school (name + city) in a static grid when there are few, with no counts or claims', () => {
    const f = open();
    http.expectOne(URL).flush({ status: true, statusCode: 200, message: 'ok', data: [school('Alpha School'), school('Beta School', { city: null })] });
    f.detectChanges();
    expect(el(f).querySelector('h2')!.textContent?.trim()).toBe('Our Partner Schools');
    const cards = [...el(f).querySelectorAll('.static .school')];
    expect(cards.length).toBe(2);
    expect(cards[0].textContent).toContain('Alpha School');
    expect(cards[0].textContent).toContain('Dehradun');
    expect(cards[1].querySelector('.city')).toBeNull();
    expect(el(f).querySelector('.marquee')).toBeNull();
    expect(el(f).textContent).not.toMatch(/\d+\+?\s+schools/i);
  });

  it('shows the initials avatar when there is no logo', () => {
    const f = open();
    http.expectOne(URL).flush({ status: true, statusCode: 200, message: 'ok', data: [school('Green Hills School')] });
    f.detectChanges();
    expect(el(f).querySelector('img')).toBeNull();
    expect(el(f).querySelector('.initials')?.textContent?.trim()).toBe('GH');
  });

  it('makes a relative logo absolute, sizes and lazy-loads it, gives it alt text, and falls back to initials when it fails', () => {
    const f = open();
    http.expectOne(URL).flush({ status: true, statusCode: 200, message: 'ok', data: [school('Alpha School', { logoUrl: '/uploads/a/logo/logo_1.png' })] });
    f.detectChanges();
    const img = el(f).querySelector('img')!;
    expect(img.getAttribute('src')).toBe(`${environment.apiUrl}/uploads/a/logo/logo_1.png`);
    expect(img.getAttribute('alt')).toBe('Alpha School logo');
    expect(img.getAttribute('loading')).toBe('lazy');
    expect(img.getAttribute('width')).toBe('56');
    expect(img.getAttribute('height')).toBe('56');

    img.dispatchEvent(new Event('error'));
    f.detectChanges();
    expect(el(f).querySelector('img')).toBeNull();
    expect(el(f).querySelector('.initials')?.textContent?.trim()).toBe('AS');
  });

  it('scrolls as a marquee with a pause button for many schools; the duplicate row is hidden from assistive technology', () => {
    const f = open();
    const many = Array.from({ length: 10 }, (_, i) => school(`School ${i + 1}`));
    http.expectOne(URL).flush({ status: true, statusCode: 200, message: 'ok', data: many });
    f.detectChanges();
    const rows = el(f).querySelectorAll('.marquee .row');
    expect(rows.length).toBe(2);
    expect(rows[0].getAttribute('aria-hidden')).toBeNull();
    expect(rows[1].getAttribute('aria-hidden')).toBe('true');
    const pause = el(f).querySelector<HTMLButtonElement>('.pause')!;
    expect(pause.textContent).toContain('Pause');
    pause.click();
    f.detectChanges();
    expect(el(f).querySelector('.marquee')!.classList.contains('paused')).toBe(true);
    expect(pause.getAttribute('aria-pressed')).toBe('true');
  });
});

describe('schoolInitials', () => {
  it.each([
    ['Green Hills School', 'GH'],
    ['The Doon School', 'DS'],
    ['Akshay', 'A'],
    ['  ', '?'],
  ])('%s -> %s', (name, expected) => expect(schoolInitials(name)).toBe(expected));
});
