import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { ToastService } from '../../../../core/services/toast.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { FeeReminderPreview, NotificationSettings } from '../../models/notification-settings.model';
import { NotificationSettingsComponent } from './notification-settings.component';
import { environment } from '../../../../../environments/environment';

const API = environment.apiUrl;
const URL = `${API}/api/NotificationSettings`;
const PREVIEW_URL = `${URL}/fee-reminders/preview`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const failure = (statusCode: number, message: string) => ({ status: false, statusCode, message, data: null });

const DEFAULTS: NotificationSettings = { feeReminderEnabled: false, feeReminderSendTime: '09:00', feeReminderGraceDays: 0, lastFeeReminderRunDate: null };
const SAVED: NotificationSettings = { feeReminderEnabled: true, feeReminderSendTime: '10:30', feeReminderGraceDays: 3, lastFeeReminderRunDate: '2026-11-18' };
const PREVIEW: FeeReminderPreview = { date: '2026-11-19', graceDays: 3, studentsToRemind: 5, withDevice: 3, withoutDevice: 2 };

describe('NotificationSettingsComponent', () => {
  let fixture: ComponentFixture<NotificationSettingsComponent>;
  let http: HttpTestingController;
  let toast: ToastService;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>; // protected members
  const button = (label: string) => [...el().querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.includes(label));
  const field = (name: string) => el().querySelector<HTMLInputElement>(`app-input[formcontrolname="${name}"] input`)!;
  const type = (name: string, value: string) => {
    const input = field(name);
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };

  /** Opens the screen; `answer` is the load's reply (settings, or a failure). */
  function open(answer: NotificationSettings | { status: number; body: object } = DEFAULTS) {
    fixture = TestBed.createComponent(NotificationSettingsComponent);
    fixture.detectChanges();
    const req = http.expectOne(URL);
    if ('status' in answer && typeof answer.status === 'number') req.flush(answer.body, { status: answer.status, statusText: 'Error' });
    else req.flush(env(answer));
    fixture.detectChanges();
  }

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'sch2026006', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([errorInterceptor])), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    toast = TestBed.inject(ToastService);
    vi.spyOn(toast, 'success');
    vi.spyOn(toast, 'error');
  });
  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
    clearSessions();
  });

  describe('loading', () => {
    it('shows the title, the explanation and the defaults (off, 09:00, 0 days) when nothing was saved', () => {
      open();
      expect(el().textContent).toContain('Notification Settings');
      expect(el().textContent).toContain('one push notification a day');
      expect(el().textContent).toContain('does not show the amount');
      expect(el().querySelector<HTMLInputElement>('input[type="checkbox"]')!.checked).toBe(false);
      expect(field('feeReminderSendTime').value).toBe('09:00');
      expect(field('feeReminderGraceDays').value).toBe('0');
    });

    it('shows saved settings and starts clean: Save disabled, no unsaved badge', () => {
      open(SAVED);
      expect(el().querySelector<HTMLInputElement>('input[type="checkbox"]')!.checked).toBe(true);
      expect(field('feeReminderSendTime').value).toBe('10:30');
      expect(field('feeReminderGraceDays').value).toBe('3');
      expect(el().textContent).toContain('2026-11-18');
      expect(el().querySelector('.unsaved')).toBeNull();
      expect(button('Save settings')!.disabled).toBe(true);
    });

    it('a failed load shows a clear message with Try again, and retrying loads the screen', () => {
      open({ status: 500, body: failure(500, 'boom') });
      expect(el().textContent).toContain('could not be loaded');
      expect(el().querySelector('form')).toBeNull();
      expect(toast.error).not.toHaveBeenCalled(); // the screen has its own message
      button('Try again')!.click();
      http.expectOne(URL).flush(env(SAVED));
      fixture.detectChanges();
      expect(el().querySelector('form')).not.toBeNull();
      expect(field('feeReminderSendTime').value).toBe('10:30');
    });
  });

  describe('403 (not a School Admin)', () => {
    it('a refused load says who may use the screen, hides the form and offers no retry', () => {
      open({ status: 403, body: failure(403, 'Only a School Admin can manage notification settings.') });
      expect(el().textContent).toContain('Only a School Admin');
      expect(el().querySelector('form')).toBeNull();
      expect(button('Try again')).toBeUndefined();
      expect(button('Save settings')!.disabled).toBe(true);
      expect(toast.error).not.toHaveBeenCalled();
    });

    it("a refused save shows the server's reason and keeps what was typed", () => {
      open(DEFAULTS);
      cmp()['form'].controls.feeReminderEnabled.setValue(true);
      fixture.detectChanges();
      button('Save settings')!.click();
      http.expectOne(URL).flush(failure(403, 'Only a School Admin can manage notification settings.'), { status: 403, statusText: 'Forbidden' });
      fixture.detectChanges();
      expect(toast.error).toHaveBeenCalledWith('Only a School Admin can manage notification settings.');
      expect(toast.success).not.toHaveBeenCalled();
      expect(cmp()['form'].controls.feeReminderEnabled.value).toBe(true);
      expect(button('Save settings')!.disabled).toBe(false); // can try again
    });
  });

  describe('saving', () => {
    it('is disabled until something changes, then PUTs the exact request and shows a clean state', () => {
      open(DEFAULTS);
      expect(button('Save settings')!.disabled).toBe(true);

      cmp()['form'].controls.feeReminderEnabled.setValue(true);
      type('feeReminderSendTime', '10:30');
      type('feeReminderGraceDays', '3');
      expect(el().querySelector('.unsaved')).not.toBeNull();
      expect(button('Save settings')!.disabled).toBe(false);

      button('Save settings')!.click();
      const req = http.expectOne(URL);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ feeReminderEnabled: true, feeReminderSendTime: '10:30', feeReminderGraceDays: 3 });
      fixture.detectChanges();
      expect(button('Saving')!.disabled).toBe(true); // loading state: no double submit
      req.flush(env(SAVED));
      fixture.detectChanges();

      expect(toast.success).toHaveBeenCalledOnce();
      expect(el().querySelector('.unsaved')).toBeNull();
      expect(button('Save settings')!.disabled).toBe(true);
    });

    it('turning a value back to what is saved disables Save again', () => {
      open(SAVED);
      type('feeReminderGraceDays', '4');
      expect(button('Save settings')!.disabled).toBe(false);
      type('feeReminderGraceDays', '3');
      expect(button('Save settings')!.disabled).toBe(true);
    });

    it("a failed save toasts the server's message and keeps the typed values", () => {
      open(DEFAULTS);
      type('feeReminderGraceDays', '5');
      button('Save settings')!.click();
      http.expectOne(URL).flush(failure(400, 'The send time must look like 09:00 (24-hour, India time).'), { status: 400, statusText: 'Bad Request' });
      fixture.detectChanges();
      expect(toast.error).toHaveBeenCalledWith('The send time must look like 09:00 (24-hour, India time).');
      expect(toast.success).not.toHaveBeenCalled();
      expect(field('feeReminderGraceDays').value).toBe('5');
      expect(button('Save settings')!.disabled).toBe(false);
    });
  });

  describe('validation (same limits as the server)', () => {
    it.each(['-1', '31', '1.5', 'abc', ''])('grace period "%s" is refused: message shown, Save disabled, nothing sent', (bad) => {
      open(DEFAULTS);
      type('feeReminderGraceDays', bad);
      expect(cmp()['form'].controls.feeReminderGraceDays.valid).toBe(false);
      expect(el().textContent).toMatch(/0 (to|and) 30/);
      expect(button('Save settings')!.disabled).toBe(true);
      cmp()['save']();
      http.expectNone(URL);
    });

    it.each(['0', '1', '30'])('grace period "%s" is accepted', (good) => {
      open(DEFAULTS);
      type('feeReminderGraceDays', good);
      expect(cmp()['form'].controls.feeReminderGraceDays.valid).toBe(true);
    });

    it.each(['', '24:00', '9:00', '09:60', 'ab:cd', '09:00:00'])('send time "%s" is refused: message shown, Save disabled, nothing sent', (bad) => {
      open(SAVED);
      type('feeReminderSendTime', bad);
      expect(cmp()['form'].controls.feeReminderSendTime.valid).toBe(false);
      expect(el().textContent).toContain('24-hour, India time');
      expect(button('Save settings')!.disabled).toBe(true);
      cmp()['save']();
      http.expectNone(URL);
    });

    it.each(['00:00', '09:05', '23:59'])('send time "%s" is accepted', (good) => {
      open(SAVED);
      type('feeReminderSendTime', good);
      expect(cmp()['form'].controls.feeReminderSendTime.valid).toBe(true);
    });
  });

  describe('Preview today', () => {
    it('GETs the preview (and sends nothing else) and shows the three counts and the note', () => {
      open(SAVED);
      button('Preview today')!.click();
      const req = http.expectOne(PREVIEW_URL);
      expect(req.request.method).toBe('GET');
      fixture.detectChanges();
      expect(button('Checking')!.disabled).toBe(true);
      req.flush(env(PREVIEW));
      fixture.detectChanges();

      const stats = [...el().querySelectorAll('.stat')].map((s) => s.textContent!.replace(/\s+/g, ' ').trim());
      expect(stats).toEqual(['5Students would be reminded', '3Have the app registered', '2Do not have the app registered']);
      expect(el().textContent).toContain('Students without the app registered will not receive anything');
      expect(button('Preview today')!.disabled).toBe(false);
    });

    it('while the form has unsaved changes it says the preview uses the saved settings', () => {
      open(SAVED);
      expect(el().textContent).not.toContain('The preview uses your saved settings');
      type('feeReminderGraceDays', '9');
      expect(el().textContent).toContain('The preview uses your saved settings');
    });

    it('a failed preview shows its own message (no stale counts) and the screen keeps working', () => {
      open(SAVED);
      button('Preview today')!.click();
      http.expectOne(PREVIEW_URL).flush(failure(500, 'boom'), { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();
      expect(el().querySelector('.error')?.textContent).toContain('preview could not be loaded');
      expect(el().querySelector('.stat')).toBeNull();
      expect(toast.error).not.toHaveBeenCalled();

      button('Preview today')!.click();
      http.expectOne(PREVIEW_URL).flush(env(PREVIEW));
      fixture.detectChanges();
      expect(el().querySelector('.error')).toBeNull();
      expect(el().querySelectorAll('.stat')).toHaveLength(3);
    });

    it("a refused preview (403) shows the server's reason", () => {
      open(SAVED);
      button('Preview today')!.click();
      http.expectOne(PREVIEW_URL).flush(failure(403, 'Only a School Admin can manage notification settings.'), { status: 403, statusText: 'Forbidden' });
      fixture.detectChanges();
      expect(el().querySelector('.error')?.textContent).toContain('Only a School Admin');
    });

    it('a successful save clears an older preview (it described the old settings)', () => {
      open(DEFAULTS);
      button('Preview today')!.click();
      http.expectOne(PREVIEW_URL).flush(env(PREVIEW));
      fixture.detectChanges();
      expect(el().querySelector('.stat')).not.toBeNull();

      type('feeReminderGraceDays', '2');
      button('Save settings')!.click();
      http.expectOne(URL).flush(env({ ...DEFAULTS, feeReminderGraceDays: 2 }));
      fixture.detectChanges();
      expect(el().querySelector('.stat')).toBeNull();
    });
  });
});
