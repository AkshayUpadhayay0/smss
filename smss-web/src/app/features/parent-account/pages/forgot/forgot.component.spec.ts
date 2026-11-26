import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ParentForgotComponent } from './forgot.component';

const URL = `${environment.apiUrl}/api/parent/auth/forgot`;

describe('ParentForgotComponent', () => {
  let fixture: ComponentFixture<ParentForgotComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ParentForgotComponent);
    fixture.detectChanges();
  });
  afterEach(() => http.verify());

  it('asks for a contact before it sends anything', () => {
    cmp()['submit'](); fixture.detectChanges();
    http.expectNone(URL);
    expect(text()).toContain('Enter your WhatsApp number or email');
  });

  it('posts the contact in the body and shows the same answer whatever it was', () => {
    cmp()['form'].setValue({ contact: ' 98765 43210 ' }); cmp()['submit']();
    const req = http.expectOne(URL);
    expect(req.request.body).toEqual({ contact: '98765 43210' });
    req.flush({ status: true, statusCode: 200, message: 'ok', data: {} });
    fixture.detectChanges();
    expect(el().querySelector('[data-state="sent"]')?.textContent).toContain('If that matches a parent account');
  });

  it('a rate limit says to wait and says nothing about the contact', () => {
    cmp()['form'].setValue({ contact: 'a@b.com' }); cmp()['submit']();
    http.expectOne(URL).flush({ status: false, statusCode: 429, message: 'x', data: null }, { status: 429, statusText: 'Too Many' });
    fixture.detectChanges();
    expect(el().querySelector('[data-note="error"]')?.textContent).toContain('Please wait a few minutes');
    expect(el().querySelector('[data-state="sent"]')).toBeNull();
  });
});
