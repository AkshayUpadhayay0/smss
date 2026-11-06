"""Verify the rewritten copy made by prepare-history-rewrite.py. Secret values are used only as search terms and never printed.
Usage: python scripts/verify-history-rewrite.py [backup folder, default E:\SecurityBackup]"""
import subprocess, re
BK = r'E:\SecurityBackup'; ORIG = BK + r'\smss-original-mirror.git'; NEW = BK + r'\smss-rewritten.git'; WC = r'E:\05_Smss\smss'
def git(d, *a, **kw):
    base = ['git', f'--git-dir={d}'] if d != WC else ['git', '-C', WC]
    return subprocess.run(base + list(a), capture_output=True, text=True, **kw).stdout
cur = subprocess.run(['dotnet', 'user-secrets', 'list', '--project', r'E:\05_Smss\smss\smss-api\smss-api'], capture_output=True, text=True).stdout
def sec(k): return re.search(re.escape(k) + r'\s*=\s*(.*)', cur).group(1).strip()
dbpw = re.search(r'Password=([^;\r\n]*)', sec('ConnectionStrings:DefaultConnection')).group(1)
checks = {'Jwt:Key': sec('Jwt:Key'), 'setup key': sec('Bootstrap:SuperAdminSetupKey'), 'SMTP app password': sec('Email:SenderAppPassword'), 'DB password (as Password=<value>)': 'Password=' + dbpw}
ok = True
def report(label, good, extra=''):
    global ok; ok &= bool(good); print(('PASS ' if good else 'FAIL ') + label + (f'  {extra}' if extra else ''))
print('--- branches and counts')
refs_o = git(ORIG, 'for-each-ref', '--format=%(refname)').split(); refs_n = git(NEW, 'for-each-ref', '--format=%(refname)').split()
print('original refs :', ' '.join(refs_o)); print('rewritten refs:', ' '.join(refs_n))
report('every original branch exists in the rewritten mirror (heads: 03_Nov, 05_Nov, main; oct_21 from the remote-tracking ref); no tags', sorted(refs_n) == sorted(['refs/heads/03_Nov', 'refs/heads/05_Nov', 'refs/heads/main', 'refs/heads/oct_21']))
no, nn = int(git(ORIG, 'rev-list', '--all', '--count')), int(git(NEW, 'rev-list', '--all', '--count'))
print(f'commits in all refs: original {no} (that count includes the remote-tracking copies), rewritten {nn}')
for b in ['main', '05_Nov', '03_Nov']:
    co = int(git(ORIG, 'rev-list', '--count', b)); cn = int(git(NEW, 'rev-list', '--count', b)); print(f'  branch {b}: {co} -> {cn} commits')
co = int(git(ORIG, 'rev-list', '--count', 'refs/remotes/origin/oct_21')); cn = int(git(NEW, 'rev-list', '--count', 'oct_21')); print(f'  branch oct_21: {co} -> {cn} commits')
print('--- secrets: no commit in any ref adds or removes the value (git log -S over everything)')
for label, v in checks.items():
    hits = git(NEW, 'log', '--all', '--format=%h', '-S' + v).split()
    hits_o = git(ORIG, 'log', '--all', '--format=%h', '-S' + v).split()
    report(f'{label}: original had it in {len(hits_o)} commits; rewritten has it in {len(hits)}', len(hits) == 0 and len(hits_o) > 0)
print('--- files: no dump, uploaded file or credentials file anywhere')
names = set(git(NEW, 'log', '--all', '--name-only', '--format=').split('\n'))
report('no path matching smss-db*, uploads/, demo-credentials in any commit', not [n for n in names if re.search(r'smss-db|/uploads/|demo-credentials', n)])
objs = git(NEW, 'rev-list', '--objects', '--all')
report('no object in the repository has such a path (rev-list --objects)', not re.search(r'smss-db|/uploads/|demo-credentials', objs))
mig = lambda d, ref: sorted(f for f in git(d, 'ls-tree', '-r', '--name-only', ref).split(chr(10)) if re.fullmatch(r'Docs/smss-(?!db).*'+chr(92)+'.sql', f))
report('migration scripts untouched: same list of Docs/smss-<feature>.sql on main and on 05_Nov as before the rewrite', mig(NEW, 'main') == mig(ORIG, 'main') and mig(NEW, '05_Nov') == mig(ORIG, '05_Nov') and len(mig(NEW, '05_Nov')) == 28, (len(mig(NEW, 'main')), len(mig(NEW, '05_Nov'))))
print('size of the rewritten repository:', git(NEW, 'count-objects', '-vH').split('size-pack:')[1].split('\n')[0].strip(), '| original:', git(ORIG, 'count-objects', '-vH').split('size-pack:')[1].split('\n')[0].strip())
print('--- content integrity')
t_new = git(NEW, 'rev-parse', '05_Nov^{tree}').strip(); t_wc = git(WC, 'rev-parse', '05_Nov^{tree}').strip()
report('05_Nov (the latest work) has exactly the same tree as your working copy branch: nothing but the removed files differed', t_new == t_wc, f'{t_new[:10]} / {t_wc[:10]}')
d = git(NEW, 'diff', '--stat', '--no-renames', '4f770ab^{tree}' if False else 'main', 'main')  # placeholder: same ref, empty
orig_main_files = set(git(ORIG, 'ls-tree', '-r', '--name-only', 'main').split('\n')); new_main_files = set(git(NEW, 'ls-tree', '-r', '--name-only', 'main').split('\n'))
removed = sorted(orig_main_files - new_main_files); added = sorted(new_main_files - orig_main_files)
report('main: the only files that disappeared are the removed ones (' + str(len(removed)) + ')', all(re.search(r'smss-db|/uploads/|demo-credentials', f) for f in removed) and not added, removed[:9])
changed = [f for f in orig_main_files & new_main_files if git(ORIG, 'rev-parse', f'main:{f}').strip() != git(NEW, 'rev-parse', f'main:{f}').strip()]
print('main: files whose content changed (only secret replacement expected):', changed)
print('--- sample messages (original -> rewritten, same position from the tip of main)')
mo = git(ORIG, 'log', 'main', '--format=%s', '-8').split('\n')[:8]; mn = git(NEW, 'log', 'main', '--format=%s', '-8').split('\n')[:8]
for a, b in zip(mo, mn): print('  ', 'same' if a == b else 'DIFF', '|', a[:70])
first_o = git(ORIG, 'log', 'main', '--reverse', '--format=%an|%ad|%s', '--date=short').split('\n')[0]; first_n = git(NEW, 'log', 'main', '--reverse', '--format=%an|%ad|%s', '--date=short').split('\n')[0]
report('first commit keeps author, date and message', first_o == first_n, first_n)
dropped = sorted(set(git(ORIG, 'log', 'main', '--format=%s').split('\n')) - set(git(NEW, 'log', 'main', '--format=%s').split('\n')))
print('commit messages that vanished from main (commits that touched only removed files):', dropped)
print('\nALL CHECKS PASS' if ok else '\nSOME CHECKS FAILED')
