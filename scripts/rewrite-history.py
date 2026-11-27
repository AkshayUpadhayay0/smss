"""Rewrite the git history in a SCRATCH MIRROR so that no secret value, database dump, uploaded file, credentials file or IDE state remains in ANY branch or tag. It never touches the repository it reads
from and never pushes.

    python scripts/rewrite-history.py <source repo> <work folder> [--extra-secrets-file FILE]

  <source repo>   the real working copy (read only: it is only cloned from). Run `git fetch origin` there first so that origin/main and origin/oct_21 are current.
  <work folder>   a NEW or empty folder OUTSIDE the source, for example E:\\SecurityBackup\\rewrite-2026-11-27. It receives
                    original.git    an untouched mirror of everything (keep it offline until you are completely sure; it still holds the secrets)
                    rewritten.git   the rewritten mirror, which is what you push (see Docs/push-plan.md)
  --extra-secrets-file   optional: a text file with one OLD secret value per line (use it for a value that was never committed in a tracked config file but must be scrubbed anyway)

The OLD secret values are read from the history itself (every value the tracked appsettings files ever held for Key, SenderAppPassword, SuperAdminSetupKey, AccessToken, AppSecret,
WebhookVerifyToken and the Password= of a connection string), so it does not matter that user-secrets now holds NEW values. A value is replaced by REDACTED-SECRET; a connection-string password is
replaced only where it follows Password= (it may be a common word). No value is ever printed. Needs: pip install git-filter-repo.
"""
import os, re, shutil, subprocess, sys, tempfile

PATHS_REMOVED = ['Docs/demo-credentials.txt', 'smss-api/smss-api/uploads', 'smss-api/smss-api/uploads-private', '.vs']
GLOBS_REMOVED = ['Docs/smss-db*', 'Docs/~$*', '*/.vs/*']
CONFIG_FILES = ['smss-api/smss-api/appsettings.json', 'smss-api/smss-api/appsettings.Development.json', 'smss-api/smss-api/appsettings.Production.json']
KEYED = re.compile(r'"(Key|SenderAppPassword|SuperAdminSetupKey|AccessToken|AppSecret|WebhookVerifyToken)"\s*:\s*"([^"]{8,})"')
CONN_PASSWORD = re.compile(r'Password=([^;"\r\n]+)', re.I)


def git(gd, *args, check=True):
    r = subprocess.run(['git', f'--git-dir={gd}', *args], capture_output=True, text=True, encoding='utf-8', errors='replace')
    if check and r.returncode != 0: raise SystemExit(f'git {" ".join(args)} failed: {r.stderr.strip()[:300]}')
    return r.stdout


def collect_secrets(gd):
    """Every value the tracked config files ever held for a secret-looking key (never printed)."""
    literals, passwords = set(), set()
    for path in CONFIG_FILES:
        for rev in git(gd, 'rev-list', '--all', '--', path).split():
            data = git(gd, 'show', f'{rev}:{path}', check=False)
            for m in KEYED.finditer(data):
                if 'REDACTED' not in m.group(2) and not m.group(2).startswith(('<', '$', '{')): literals.add(m.group(2))
            for m in CONN_PASSWORD.finditer(data):
                if 'REDACTED' not in m.group(1) and not m.group(1).startswith(('<', '$', '{', '*')) and len(m.group(1)) >= 3: passwords.add(m.group(1))
    return literals, passwords


def main(argv):
    extra = None
    if '--extra-secrets-file' in argv:
        i = argv.index('--extra-secrets-file'); extra = argv[i + 1]; del argv[i:i + 2]
    if len(argv) != 2: raise SystemExit(__doc__)
    source, work = os.path.abspath(argv[0]), os.path.abspath(argv[1])
    if not os.path.isdir(os.path.join(source, '.git')): raise SystemExit('The source must be a normal working copy (it has a .git folder).')
    if work == source or work.startswith(source + os.sep) or source.startswith(work + os.sep): raise SystemExit('The work folder must be outside the source repository.')
    if os.path.exists(work) and os.listdir(work): raise SystemExit('The work folder must be new or empty.')
    os.makedirs(work, exist_ok=True)
    original, rewritten = os.path.join(work, 'original.git'), os.path.join(work, 'rewritten.git')

    subprocess.run(['git', 'clone', '-q', '--mirror', source, original], check=True)          # read only: the source is only cloned FROM
    subprocess.run(['git', 'clone', '-q', '--mirror', original, rewritten], check=True)
    # the remote-tracking branches of the working copy (for example origin/oct_21, which exists nowhere else) become real branches; the tracking refs and the origin link go
    refs = git(rewritten, 'for-each-ref', '--format=%(refname)', 'refs/remotes').split()
    existing = set(git(rewritten, 'for-each-ref', '--format=%(refname:short)', 'refs/heads').split())
    for r in refs:
        name = r.split('/', 3)[3] if r.count('/') >= 3 else ''
        if name and name != 'HEAD' and name not in existing: git(rewritten, 'branch', name, r)
    for r in refs: git(rewritten, 'update-ref', '--no-deref', '-d', r)
    git(rewritten, 'remote', 'remove', 'origin', check=False)

    literals, passwords = collect_secrets(original)
    if extra:
        literals |= {l.strip() for l in open(extra, encoding='utf-8').read().splitlines() if len(l.strip()) >= 8}
    lines = [f'literal:{v}==>REDACTED-SECRET' for v in sorted(literals)]
    lines += ['regex:(?i)(Password=)' + re.escape(p) + r'(?=[;"\'\s\\])==>\1REDACTED' for p in sorted(passwords)]
    repl = os.path.join(tempfile.gettempdir(), 'smss_repl_tmp.txt')
    open(repl, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines) + '\n')
    try:
        cmd = ['git', 'filter-repo', '--force', '--invert-paths']
        for p in PATHS_REMOVED: cmd += ['--path', p]
        for g in GLOBS_REMOVED: cmd += ['--path-glob', g]
        cmd += ['--replace-text', repl]
        r = subprocess.run(cmd, cwd=rewritten, env=dict(os.environ, GIT_DIR=rewritten), capture_output=True, text=True)
        print((r.stdout + r.stderr)[-400:])
        if r.returncode != 0: raise SystemExit('git filter-repo failed')
    finally:
        if os.path.exists(repl): os.remove(repl)
    print(f'secret values replaced: {len(literals)} literal, {len(passwords)} connection-string password(s) (values not shown)')
    for label, gd in (('original ', original), ('rewritten', rewritten)):
        heads = git(gd, 'for-each-ref', '--format=%(refname:short)', 'refs/heads', 'refs/tags').split()
        print(f'{label}: refs={heads} commits={len(git(gd, "rev-list", "--all").split())} pack={git(gd, "count-objects", "-v").split("size-pack: ")[1].split()[0]} KiB')
    print('Next: python scripts/verify-rewritten-history.py', rewritten, original)


if __name__ == '__main__':
    main(sys.argv[1:])
