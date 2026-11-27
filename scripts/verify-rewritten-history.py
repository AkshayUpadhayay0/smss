"""Verify a rewritten mirror made by scripts/rewrite-history.py. Reads only; prints paths, counts and verdicts, NEVER a secret value.

    python scripts/verify-rewritten-history.py <rewritten.git> <original.git>

Checks (exit code 0 only when every one passes):
  1. no blob in any branch or tag has a removed path (database dumps, uploads, credentials file, IDE state, lock files of Office)
  2. no blob and no commit message contains any secret value that the ORIGINAL history ever held in a tracked config file (re-extracted from the original), nor `Password=<that password>`
  3. no private-key block, Meta/GitHub/AWS/Slack token or password-looking JSON value remains in any blob (the Firebase client API key in google-services.json is reported, not failed)
  4. for every branch: the tip tree of the rewritten branch equals the original's, OR the differing files are listed (a tip that held a secret differs only in those files)
  5. branches and tags are the same names; commit count before and after; the history is connected (git fsck)
"""
import json, os, re, subprocess, sys, collections

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
BANNED = re.compile(r'(^|/)(smss-db[^/]*\.sql|demo-credentials\.txt|~\$[^/]*|\.vs/.*|\.vs)$|(^|/)uploads(-private)?/|\.(dump|bak|pgdump|dmp)$')
PATTERNS = {
    'private-key block': re.compile(rb'-----BEGIN [A-Z ]*PRIVATE KEY-----'),
    'github token': re.compile(rb'gh[pousr]_[0-9A-Za-z]{30,}'),
    'aws key': re.compile(rb'AKIA[0-9A-Z]{16}'),
    'slack token': re.compile(rb'xox[baprs]-[0-9A-Za-z\-]{10,}'),
    'meta token': re.compile(rb'(?<![A-Za-z0-9+/])EAA(?!AAA)[A-Za-z0-9]{60,}'),
    'secret-looking json value': re.compile(rb'(?i)"(?:senderapppassword|superadminsetupkey|appsecret|accesstoken|webhookverifytoken)"\s*:\s*"(?!REDACTED)[^"\s]{8,}"'),
    'database dump': re.compile(rb'PGDMP|-- PostgreSQL database dump'),
}
ALLOWED_PATHS_FOR_DUMP_TEXT = re.compile(r'^(\.githooks/|\.gitleaks\.toml|Docs/|scripts/|smss-api/smss-api-tests/)')


def git(gd, *args, binary=False, check=True):
    r = subprocess.run(['git', f'--git-dir={gd}', *args], capture_output=True)
    if check and r.returncode != 0: raise SystemExit(f'git {" ".join(args)} failed')
    return r.stdout if binary else r.stdout.decode('utf-8', 'replace')


def main(argv):
    if len(argv) != 2: raise SystemExit(__doc__)
    new, old = os.path.abspath(argv[0]), os.path.abspath(argv[1])
    import importlib.util
    spec = importlib.util.spec_from_file_location('rewrite_history', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'rewrite-history.py'))
    rh = importlib.util.module_from_spec(spec); spec.loader.exec_module(rh)
    literals, passwords = rh.collect_secrets(old)
    needles = [v.encode() for v in literals] + [b'Password=' + p.encode() for p in passwords]
    ok = True
    def verdict(name, good, detail=''):
        nonlocal ok
        ok &= good
        print(('PASS ' if good else 'FAIL ') + name + (': ' + detail if detail else ''))

    objs = git(new, 'rev-list', '--objects', '--all').splitlines()
    paths = collections.defaultdict(set)
    for l in objs:
        p = l.split(' ', 1)
        if len(p) == 2: paths[p[0]].add(p[1])
    types = {}
    batch = subprocess.run(['git', f'--git-dir={new}', 'cat-file', '--batch-check=%(objectname) %(objecttype)'], input='\n'.join(paths).encode(), capture_output=True).stdout.decode().splitlines()
    for l in batch:
        sha, t = l.split(); types[sha] = t
    blobs = [s for s, t in types.items() if t == 'blob']

    bad_paths = sorted({p for s in blobs for p in paths[s] if BANNED.search(p)})
    verdict('1. no removed path is left in any ref', not bad_paths, ', '.join(bad_paths[:6]))

    proc = subprocess.Popen(['git', f'--git-dir={new}', 'cat-file', '--batch'], stdin=subprocess.PIPE, stdout=subprocess.PIPE)
    hit_secret, hit_pattern, notes = set(), collections.defaultdict(set), set()
    for sha in blobs:
        proc.stdin.write(sha.encode() + b'\n'); proc.stdin.flush()
        size = int(proc.stdout.readline().split()[2]); data = proc.stdout.read(size); proc.stdout.read(1)
        if any(n in data for n in needles): hit_secret |= paths[sha]
        for name, rx in PATTERNS.items():
            if rx.search(data):
                if name == 'database dump' and all(ALLOWED_PATHS_FOR_DUMP_TEXT.match(p) for p in paths[sha]): continue   # a hook, a doc or a test that names the marker
                hit_pattern[name] |= paths[sha]
    proc.stdin.close(); proc.wait()
    messages = git(new, 'log', '--all', '--format=%B', binary=True)
    verdict('2. no old secret value in any file', not hit_secret, ', '.join(sorted(hit_secret)[:6]))
    verdict('2b. no old secret value in any commit message', not any(n in messages for n in needles))
    verdict('3. no key block, token or secret-looking value in any file', not hit_pattern, '; '.join(f'{k}: {sorted(v)[:3]}' for k, v in hit_pattern.items()))

    heads_new = set(git(new, 'for-each-ref', '--format=%(refname)', 'refs/heads', 'refs/tags').split())
    heads_old = set(git(old, 'for-each-ref', '--format=%(refname)', 'refs/heads', 'refs/tags').split())
    only_remote = {r for r in git(old, 'for-each-ref', '--format=%(refname)', 'refs/remotes').split() if not r.endswith('/HEAD')}
    expected = heads_old | {'refs/heads/' + r.split('/', 3)[3] for r in only_remote}
    verdict('5. the same branches and tags (remote-only branches are now branches)', heads_new == expected, f'new={sorted(heads_new)}')
    for ref in sorted(heads_new):
        name = ref.split('/', 2)[2]
        old_ref = ref if ref in heads_old else 'refs/remotes/origin/' + name
        a, b = git(old, 'ls-tree', '-r', old_ref).splitlines(), git(new, 'ls-tree', '-r', ref).splitlines()
        da = {l.split('\t')[1]: l.split()[2] for l in a}; db = {l.split('\t')[1]: l.split()[2] for l in b}
        removed = sorted(set(da) - set(db)); changed = sorted(p for p in da if p in db and da[p] != db[p])
        print(f'     tip of {name}: {"identical" if not removed and not changed else "differs"} (removed {len(removed)}, redacted/changed {len(changed)}: {", ".join((removed + changed)[:5])})')
    n_old, n_new = len(git(old, 'rev-list', '--all').split()), len(git(new, 'rev-list', '--all').split())
    print(f'     commits: {n_old} before, {n_new} after (a commit that only added a removed file disappears)')
    fsck = subprocess.run(['git', f'--git-dir={new}', 'fsck', '--no-dangling'], capture_output=True, text=True)
    verdict('5b. the rewritten history is intact (git fsck)', fsck.returncode == 0, fsck.stderr.strip()[:120])
    print('RESULT:', 'ALL CHECKS PASSED' if ok else 'FAILED')
    sys.exit(0 if ok else 1)


if __name__ == '__main__':
    main(sys.argv[1:])
