// Expo config plugin: pins the CMake version the Android build uses, so the Windows long-path fix survives `expo prebuild --clean` (the android/ folder is generated and git-ignored).
//
// WHY: on Windows the first `npx expo run:android` can fail in `buildCMakeDebug` with "ninja: error: Stat(...)": the generated codegen object paths are longer than
// 260 characters and the default CMake 3.22.1 ships ninja 1.10, which has no long-path support. CMake 4.1.2 ships ninja 1.12, which does (Windows must have
// LongPathsEnabled = 1, which is already the case on this project's PC).
//
// It is OFF unless SMSS_ANDROID_CMAKE_VERSION is set (see app.config.ts), so macOS / Linux / CI builds are untouched. Idempotent: running prebuild twice changes nothing.
const { withAppBuildGradle } = require('expo/config-plugins');

const MARKER = '// smss: pinned CMake version (see plugins/withAndroidCmakeVersion.js)';

/** Pure: returns the app/build.gradle text with exactly one pinned-CMake block inside `android { }`. */
function applyCmakeVersion(contents, version) {
  if (!/^\d+\.\d+\.\d+(\.\d+)?$/.test(version)) throw new Error(`SMSS_ANDROID_CMAKE_VERSION must look like 4.1.2, got "${version}"`);
  const eol = contents.includes('\r\n') ? '\r\n' : '\n';

  if (contents.includes(MARKER)) {
    // already there: only the version may change
    const existing = /(version = ")[^"\r\n]*(")/;
    const from = contents.indexOf(MARKER);
    const head = contents.slice(0, from);
    const tail = contents.slice(from).replace(existing, `$1${version}$2`);
    return head + tail;
  }

  const lines = contents.split(eol);
  const at = lines.findIndex((l) => /^android \{\s*$/.test(l));
  if (at < 0) throw new Error('withAndroidCmakeVersion: could not find the android { } block in app/build.gradle');
  const block = [`    ${MARKER}`, '    externalNativeBuild {', '        cmake {', `            version = "${version}"`, '        }', '    }'];
  lines.splice(at + 1, 0, ...block);
  return lines.join(eol);
}

function withAndroidCmakeVersion(config, props) {
  const version = props && props.version;
  if (!version) return config;
  return withAppBuildGradle(config, (cfg) => {
    cfg.modResults.contents = applyCmakeVersion(cfg.modResults.contents, version);
    return cfg;
  });
}

module.exports = withAndroidCmakeVersion;
module.exports.applyCmakeVersion = applyCmakeVersion;
