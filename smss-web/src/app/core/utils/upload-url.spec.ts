import { environment } from '../../../environments/environment';
import { initialsOf, toUploadUrl } from './upload-url';

describe('upload-url', () => {
  it('makes a relative upload path absolute and leaves absolute URLs alone', () => {
    expect(toUploadUrl('/uploads/123/students/5/photo_1.png')).toBe(`${environment.apiUrl}/uploads/123/students/5/photo_1.png`);
    expect(toUploadUrl('https://cdn.example.com/a.png')).toBe('https://cdn.example.com/a.png');
  });

  it('returns null when there is no file', () => {
    expect(toUploadUrl(null)).toBeNull();
    expect(toUploadUrl('')).toBeNull();
    expect(toUploadUrl(undefined)).toBeNull();
  });

  it('builds placeholder initials', () => {
    expect(initialsOf('Asha K Verma')).toBe('AV');
    expect(initialsOf('Madonna')).toBe('M');
    expect(initialsOf('  zed   ten ')).toBe('ZT');
    expect(initialsOf('')).toBe('?');
    expect(initialsOf(null)).toBe('?');
  });
});
