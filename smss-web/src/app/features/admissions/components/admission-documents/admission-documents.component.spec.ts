import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { AdmissionDocumentsComponent } from './admission-documents.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
const doc = (id: number, type: string, name: string) => ({
  applicationDocumentId: id, documentTypeId: id, documentTypeName: type, fileName: name, contentType: 'application/pdf', fileSize: 2048, remarks: null, uploadedByName: 'Asha Staff', uploadedAt: '2026-10-22T04:00:00Z', statusName: 'Active',
});
const type = (id: number, name: string, appliesTo: string, isActive = true, isRequired = false) => ({ documentTypeId: id, schoolId: 'S', documentName: name, documentCode: name.slice(0, 3), appliesTo, isRequired, isActive, createdAt: '', updatedAt: '' });

describe('AdmissionDocumentsComponent', () => {
  let fixture: ComponentFixture<AdmissionDocumentsComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(locked = false, docs = [doc(1, 'Marksheet', 'marks.pdf'), doc(2, 'Aadhaar', 'aadhaar.pdf')]) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Admission Staff'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AdmissionDocumentsComponent);
    fixture.componentRef.setInput('admissionId', 7);
    fixture.componentRef.setInput('locked', locked);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Admission/S/7/documents`).flush(env(docs));
    http.expectOne(`${API}/api/DocumentType/S`).flush(env([type(1, 'Marksheet', 'STUDENT'), type(2, 'Aadhaar', 'BOTH', true, true), type(3, 'Resume', 'EMPLOYEE'), type(4, 'Retired', 'STUDENT', false)]));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('lists the documents with their type, file name and size; the type choices are only ACTIVE types for students (STUDENT or BOTH)', () => {
    open();
    expect([...el().querySelectorAll('.docs li strong')].map((s) => s.textContent)).toEqual(['Marksheet', 'Aadhaar']);
    expect(el().textContent).toContain('marks.pdf · 2.0 KB');
    expect(cmp()['typeOptions']().map((o: { label: string }) => o.label)).toEqual(['Marksheet', 'Aadhaar (required)']);
  });

  it('Upload sends the file, the chosen type and the remarks, then adds the document to the top of the list and clears the form', () => {
    open();
    cmp()['typeControl'].setValue('2');
    cmp()['remarksControl'].setValue(' Front and back ');
    cmp()['file'].set(new File(['%PDF-1.4'], 'new.pdf', { type: 'application/pdf' }));
    cmp()['upload']();
    const post = http.expectOne(`${API}/api/Admission/S/7/documents`);
    const form = post.request.body as FormData;
    expect(form.get('documentTypeId')).toBe('2');
    expect(form.get('remarks')).toBe('Front and back');
    expect((form.get('file') as File).name).toBe('new.pdf');
    post.flush(env(doc(9, 'Aadhaar', 'new.pdf'), 201));
    fixture.detectChanges();
    expect(cmp()['documents']()[0].applicationDocumentId).toBe(9);
    expect(cmp()['typeControl'].value).toBe('');
    expect(cmp()['file']()).toBeNull();
  });

  it('refuses without a type or a file, a wrong extension and an oversized file - nothing is sent', () => {
    open();
    cmp()['upload']();
    expect(cmp()['typeError']()).toBe('Select a document type');
    expect(cmp()['fileError']()).toBe('Choose a file');
    const pick = (f: File) => cmp()['onFile']({ target: { files: [f], value: '' } } as unknown as Event);
    pick(new File(['x'], 'run.exe')); expect(cmp()['fileError']()).toContain('Only PDF');
    pick(new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'big.pdf')); expect(cmp()['fileError']()).toContain('5 MB');
    pick(new File([], 'empty.pdf')); expect(cmp()['fileError']()).toContain('empty');
    http.expectNone(`${API}/api/Admission/S/7/documents`);
  });

  it('Download fetches the bytes through the authenticated endpoint and saves them under the file name', () => {
    const createUrl = vi.fn(() => 'blob:x');
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: createUrl, revokeObjectURL: vi.fn() }));
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    open();
    cmp()['download'](cmp()['documents']()[0]);
    const req = http.expectOne(`${API}/api/Admission/S/7/documents/1/download`);
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['%PDF']));
    expect(createUrl).toHaveBeenCalled();
    expect(click).toHaveBeenCalled();
    click.mockRestore();
    vi.unstubAllGlobals();
  });

  it('Remove confirms first, then DELETEs and drops the row; declining sends nothing', async () => {
    open();
    const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    await cmp()['remove'](cmp()['documents']()[0]);
    http.expectNone((r) => r.method === 'DELETE');
    const done = cmp()['remove'](cmp()['documents']()[0]);
    await Promise.resolve();
    const del = http.expectOne(`${API}/api/Admission/S/7/documents/1`);
    expect(del.request.method).toBe('DELETE');
    del.flush(env({ ...doc(1, 'Marksheet', 'marks.pdf'), statusName: 'Inactive' }));
    await done;
    expect(confirm).toHaveBeenCalledTimes(2);
    expect(cmp()['documents']().map((d: { applicationDocumentId: number }) => d.applicationDocumentId)).toEqual([2]);
  });

  it('a closed application (Rejected / Withdrawn / Admitted) shows the documents read-only: no upload form, no Remove', () => {
    open(true);
    expect(el().querySelector('input[type=file]')).toBeNull();
    expect(el().textContent).toContain('documents can only be viewed and downloaded');
    expect([...el().querySelectorAll('button')].map((b) => b.textContent?.trim())).toEqual(['Download', 'Download']);
  });

  it('says so when there are no documents', () => {
    open(false, []);
    expect(el().textContent).toContain('No documents uploaded yet');
  });
});
