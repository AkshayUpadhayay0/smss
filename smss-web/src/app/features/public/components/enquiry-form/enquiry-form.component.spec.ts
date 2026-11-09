import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { PublicEnquiryService } from '../../services/public-enquiry.service';
import { EnquiryFormComponent } from './enquiry-form.component';

const URL = `${environment.apiUrl}/api/public/enquiries`;

describe('EnquiryFormComponent', () => {
  let http: HttpTestingController;

  function open(type: 'CONTACT' | 'DEMO'): ComponentFixture<EnquiryFormComponent> {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(EnquiryFormComponent);
    fixture.componentRef.setInput('type', type);
    fixture.detectChanges();
    return fixture;
  }
  const el = (f: ComponentFixture<unknown>) => f.nativeElement as HTMLElement;
  const text = (f: ComponentFixture<unknown>) => (el(f).textContent ?? '').replace(/\s+/g, ' ');
  function type(f: ComponentFixture<unknown>, label: string, value: string) {
    const field = [...el(f).querySelectorAll('.field')].find((x) => x.querySelector('label')?.textContent?.startsWith(label));
    const input = field!.querySelector('input, textarea, select') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event(input.tagName === 'SELECT' ? 'change' : 'input'));
  }
  const submit = (f: ComponentFixture<unknown>) => {
    el(f).querySelector('form')!.dispatchEvent(new Event('submit'));
    f.detectChanges();
  };
  const consent = (f: ComponentFixture<unknown>) => {
    const box = el(f).querySelector<HTMLInputElement>('.consent input')!;
    box.checked = true;
    box.dispatchEvent(new Event('change'));
  };

  afterEach(() => http.verify());

  it('Contact: shows the contact fields only, and no request is made while required fields are empty', () => {
    const f = open('CONTACT');
    expect(text(f)).toContain('Subject');
    expect(text(f)).not.toContain('School name');
    submit(f);
    http.expectNone(URL);
    expect(text(f)).toContain('This field is required.');
    expect(text(f)).toContain('Please accept the privacy policy to continue.');
  });

  it('Demo: shows the demo fields and requires school name and phone', () => {
    const f = open('DEMO');
    expect(text(f)).toContain('School name');
    expect(text(f)).toContain('Approximate number of students');
    expect(text(f)).not.toContain('Subject');
    submit(f);
    http.expectNone(URL);
  });

  it('rejects a malformed email and phone with a clear message', () => {
    const f = open('DEMO');
    type(f, 'Email', 'not-an-email');
    type(f, 'Phone', 'abc');
    submit(f);
    expect(text(f)).toContain('Enter a valid email address.');
    expect(text(f)).toContain('Enter a valid phone number.');
  });

  it('sends a valid contact message once (double submit ignored), then shows the success state', () => {
    const f = open('CONTACT');
    type(f, 'Full name', 'Asha Rao');
    type(f, 'Email', 'asha@example.com');
    type(f, 'Message', 'Hello');
    consent(f);
    submit(f);
    submit(f); // a second click while the first is in flight
    const req = http.expectOne(URL);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toMatchObject({ enquiryType: 'CONTACT', fullName: 'Asha Rao', email: 'asha@example.com', message: 'Hello', consentGiven: true, website: '' });
    expect(el(f).querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(true);
    req.flush({ status: true, statusCode: 201, message: 'ok', data: null });
    f.detectChanges();
    expect(text(f)).toContain('Message sent');
    expect(el(f).querySelector('form')).toBeNull();
  });

  it('shows a friendly message and keeps the form on failure, without technical detail', () => {
    const f = open('CONTACT');
    type(f, 'Full name', 'Asha Rao');
    type(f, 'Email', 'asha@example.com');
    type(f, 'Message', 'Hello');
    consent(f);
    submit(f);
    http.expectOne(URL).flush({ status: false, statusCode: 500, message: 'NpgsqlException: relation does not exist', data: null }, { status: 500, statusText: 'Server Error' });
    f.detectChanges();
    expect(text(f)).toContain('Sorry, we could not send your request right now.');
    expect(text(f)).not.toContain('Npgsql');
    expect(el(f).querySelector('form')).not.toBeNull();
    expect(el(f).querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(false);
  });

  it('shows the server\'s field messages from a 400', () => {
    const f = open('CONTACT');
    type(f, 'Full name', 'Asha Rao');
    type(f, 'Email', 'asha@example.com');
    type(f, 'Message', 'Hello');
    consent(f);
    submit(f);
    http.expectOne(URL).flush({ status: false, statusCode: 400, message: 'x', data: { email: 'Enter a valid email address.' } }, { status: 400, statusText: 'Bad Request' });
    f.detectChanges();
    expect(text(f)).toContain('Enter a valid email address.');
  });
});

describe('PublicEnquiryService.toFailure', () => {
  const service = () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    return TestBed.inject(PublicEnquiryService);
  };

  it('maps 429 to a wait-and-retry message', () => {
    const f = service().toFailure(new HttpErrorResponse({ status: 429, error: { status: false, statusCode: 429, message: 'x', data: null } }));
    expect(f.rateLimited).toBe(true);
    expect(f.message).toContain('wait a few minutes');
  });

  it('never exposes anything but the generic message for other failures', () => {
    const f = service().toFailure(new HttpErrorResponse({ status: 500, error: { message: 'stack trace here' } }));
    expect(f.message).toBe('Sorry, we could not send your request right now. Please try again in a little while.');
    expect(f.fields).toEqual({});
  });
});
