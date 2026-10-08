// Client-side mirror of the API's LogoRules: 2 MB max, PNG / JPG / WEBP, and the file's real signature
// must match its extension (the extension alone is client-controlled).

export const LOGO_MAX_BYTES = 2 * 1024 * 1024;
export const LOGO_ACCEPT = '.png,.jpg,.jpeg,.webp';
const ALLOWED = ['.png', '.jpg', '.jpeg', '.webp'];

export function extensionOf(name: string): string {
  const i = name.lastIndexOf('.');
  return i < 0 ? '' : name.slice(i).toLowerCase();
}

export function signatureMatches(extension: string, b: Uint8Array): boolean {
  const ascii = (from: number, text: string) => [...text].every((ch, i) => b[from + i] === ch.charCodeAt(0));
  switch (extension) {
    case '.png':
      return b.length >= 8 && [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a].every((v, i) => b[i] === v);
    case '.jpg':
    case '.jpeg':
      return b.length >= 3 && b[0] === 0xff && b[1] === 0xd8 && b[2] === 0xff;
    case '.webp':
      return b.length >= 12 && ascii(0, 'RIFF') && ascii(8, 'WEBP');
    default:
      return false;
  }
}

/** Resolves to an error message, or null when the file is acceptable. */
export async function validateLogoFile(file: File): Promise<string | null> {
  if (file.size <= 0) return 'The selected file is empty.';
  if (file.size > LOGO_MAX_BYTES) return 'Logo must be 2 MB or smaller.';
  const ext = extensionOf(file.name);
  if (!ALLOWED.includes(ext)) return 'Only PNG, JPG or WEBP images are allowed.';
  const header = new Uint8Array(await file.slice(0, 12).arrayBuffer());
  return signatureMatches(ext, header) ? null : 'The file is not a valid PNG, JPG or WEBP image.';
}
