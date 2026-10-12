import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { StudentPhoto } from '../../models/student.model';
import { StudentPhotoDialogComponent } from './student-photo-dialog.component';

const API = 'https://localhost:7037';
const env = <T>(data: T, statusCode = 200, status = true) => ({ status, statusCode, message: 'ok', data });

describe('StudentPhotoDialogComponent', () => {
  let fixture: ComponentFixture<StudentPhotoDialogComponent>;
  let http: HttpTestingController;
  let saved: StudentPhoto[];
  let closed: number;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>; // protected members
  const saveButton = () =>
    [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find((b) => b.textContent?.includes('Save photo')) as HTMLButtonElement;

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'sch2026006', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StudentPhotoDialogComponent);
    fixture.componentRef.setInput('studentId', 5);
    fixture.componentRef.setInput('studentName', 'Asha Verma');
    fixture.componentRef.setInput('currentUrl', `${API}/uploads/1/students/5/photo_1.png`);
    saved = [];
    closed = 0;
    fixture.componentInstance.saved.subscribe((p) => saved.push(p));
    fixture.componentInstance.closed.subscribe(() => closed++);
    fixture.detectChanges();
  });
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('cannot save until a file is picked or the photo is marked for removal', () => {
    expect(saveButton().disabled).toBe(true);
  });

  it('uploads the picked file to the student photo endpoint (school from the session)', () => {
    const file = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'me.png', { type: 'image/png' });
    cmp()['change'].set({ file, remove: false });
    fixture.detectChanges();
    expect(saveButton().disabled).toBe(false);

    cmp()['save']();
    const req = http.expectOne(`${API}/api/Student/sch2026006/5/photo`);
    expect(req.request.method).toBe('POST');
    expect((req.request.body as FormData).get('file')).toBeInstanceOf(File);
    req.flush(env({ studentId: 5, profilePhotoUrl: '/uploads/1/students/5/photo_2.png' }));

    expect(saved).toEqual([{ studentId: 5, profilePhotoUrl: '/uploads/1/students/5/photo_2.png' }]);
  });

  it('removes the photo through the remove endpoint', () => {
    cmp()['change'].set({ file: null, remove: true });
    cmp()['save']();
    const req = http.expectOne(`${API}/api/Student/sch2026006/5/photo/remove`);
    expect(req.request.method).toBe('POST');
    req.flush(env({ studentId: 5, profilePhotoUrl: null }));
    expect(saved).toEqual([{ studentId: 5, profilePhotoUrl: null }]);
  });

  it('stays open and emits nothing when the server rejects the file', () => {
    cmp()['change'].set({ file: new File([new Uint8Array([1])], 'x.png'), remove: false });
    cmp()['save']();
    http
      .expectOne(`${API}/api/Student/sch2026006/5/photo`)
      .flush({ status: false, statusCode: 400, message: 'The file is not a valid PNG, JPG or WEBP image.', data: null }, { status: 400, statusText: 'Bad Request' });
    expect(saved).toEqual([]);
    expect(closed).toBe(0);
    expect(cmp()['saving']()).toBe(false);
  });
});
