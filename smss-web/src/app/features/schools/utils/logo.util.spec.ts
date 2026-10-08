import { LOGO_MAX_BYTES, validateLogoFile } from './logo.util';

const file = (bytes: number[], name: string, pad = 0) => new File([new Uint8Array([...bytes, ...new Array(pad).fill(0)])], name);

const PNG = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
const JPG = [0xff, 0xd8, 0xff, 0xe0];
const WEBP = [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50];

describe('validateLogoFile', () => {
  it('accepts PNG, JPG/JPEG and WEBP whose signature matches', async () => {
    expect(await validateLogoFile(file(PNG, 'a.png'))).toBeNull();
    expect(await validateLogoFile(file(JPG, 'a.jpg'))).toBeNull();
    expect(await validateLogoFile(file(JPG, 'A.JPEG'))).toBeNull();
    expect(await validateLogoFile(file(WEBP, 'a.webp'))).toBeNull();
  });

  it('rejects unsupported extensions', async () => {
    expect(await validateLogoFile(file(PNG, 'a.gif'))).toMatch(/PNG, JPG or WEBP/);
    expect(await validateLogoFile(file(PNG, 'noextension'))).toMatch(/PNG, JPG or WEBP/);
  });

  it('rejects files over 2 MB and empty files', async () => {
    expect(await validateLogoFile(file(PNG, 'a.png', LOGO_MAX_BYTES))).toMatch(/2 MB/);
    expect(await validateLogoFile(new File([], 'a.png'))).toMatch(/empty/);
    expect(await validateLogoFile(file(PNG, 'a.png', LOGO_MAX_BYTES - PNG.length))).toBeNull(); // exactly 2 MB is fine
  });

  it('rejects a file whose content does not match its extension', async () => {
    expect(await validateLogoFile(file(JPG, 'fake.png'))).toMatch(/not a valid/);
    expect(await validateLogoFile(file([0x00, 0x01, 0x02, 0x03], 'fake.webp'))).toMatch(/not a valid/);
  });
});
