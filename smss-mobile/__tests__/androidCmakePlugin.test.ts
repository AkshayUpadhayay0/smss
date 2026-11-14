// eslint-disable-next-line @typescript-eslint/no-require-imports
const { applyCmakeVersion } = require('../plugins/withAndroidCmakeVersion') as { applyCmakeVersion: (contents: string, version: string) => string };

const GRADLE = `plugins {\n    id("com.android.application")\n}\n\nandroid {\n    ndkVersion rootProject.ext.ndkVersion\n    compileSdk rootProject.ext.compileSdkVersion\n}\n\ndependencies {\n}\n`;

describe('withAndroidCmakeVersion', () => {
  it('adds a pinned CMake version inside android { }', () => {
    const out = applyCmakeVersion(GRADLE, '4.1.2');
    expect(out).toMatch(/android \{\n\s*\/\/ smss: pinned CMake version[^\n]*\n\s*externalNativeBuild \{\n\s*cmake \{\n\s*version = "4\.1\.2"/);
    expect(out.indexOf('externalNativeBuild')).toBeGreaterThan(out.indexOf('android {'));
    expect(out).toContain('ndkVersion rootProject.ext.ndkVersion');           // the rest is untouched
  });
  it('is idempotent: applying it twice changes nothing', () => {
    const once = applyCmakeVersion(GRADLE, '4.1.2');
    expect(applyCmakeVersion(once, '4.1.2')).toBe(once);
    expect((once.match(/externalNativeBuild/g) ?? []).length).toBe(1);
  });
  it('changing the version rewrites the one block', () => {
    const out = applyCmakeVersion(applyCmakeVersion(GRADLE, '4.1.2'), '3.31.6');
    expect(out).toContain('version = "3.31.6"');
    expect(out).not.toContain('4.1.2');
    expect((out.match(/externalNativeBuild/g) ?? []).length).toBe(1);
  });
  it('keeps Windows line endings', () => {
    const crlf = GRADLE.replace(/\n/g, '\r\n');
    const out = applyCmakeVersion(crlf, '4.1.2');
    expect(out).not.toMatch(/[^\r]\n/);
  });
  it('refuses a malformed version and a file without an android block', () => {
    expect(() => applyCmakeVersion(GRADLE, '4.1')).toThrow(/must look like/);
    expect(() => applyCmakeVersion(GRADLE, '"; rm -rf /')).toThrow(/must look like/);
    expect(() => applyCmakeVersion('dependencies {}', '4.1.2')).toThrow(/android \{ \}/);
  });
});
