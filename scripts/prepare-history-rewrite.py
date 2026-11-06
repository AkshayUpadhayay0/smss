"""Prepare (do NOT push) a rewritten copy of the git history: no dump, no uploaded file, no demo-credentials file, and the four secret values replaced.
Usage: python scripts/prepare-history-rewrite.py [backup folder, default E:\SecurityBackup]
It (1) makes sure an untouched mirror backup exists, (2) copies it, (3) runs git filter-repo on the COPY. The old secret values are read from user-secrets into a temporary
replacement file that is deleted at the end, so run it while the OLD values are still stored there. Needs: pip install git-filter-repo. It never pushes.
Then run scripts/verify-history-rewrite.py."""
import subprocess, re, os, shutil, sys, tempfile
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BK = sys.argv[1] if len(sys.argv) > 1 else r'E:\SecurityBackup'; ORIG = os.path.join(BK, 'smss-original-mirror.git'); NEW = os.path.join(BK, 'smss-rewritten.git')
APP = r'E:\05_Smss\smss\smss-api\smss-api'
REPL = os.path.join(tempfile.gettempdir(), 'smss_repl_tmp.txt')
os.makedirs(BK, exist_ok=True)
if not os.path.isdir(ORIG): subprocess.run(['git', 'clone', '-q', '--mirror', ROOT, ORIG], check=True)    # the untouched backup, made once and never modified
cur = subprocess.run(['dotnet', 'user-secrets', 'list', '--project', APP], capture_output=True, text=True).stdout
def sec(k):
    m = re.search(re.escape(k) + r'\s*=\s*(.*)', cur); return m.group(1).strip()
conn = sec('ConnectionStrings:DefaultConnection'); dbpw = re.search(r'Password=([^;\r\n]*)', conn).group(1)
lines = [
    f'literal:{sec("Jwt:Key")}==>REDACTED-JWT-KEY',
    f'literal:{sec("Bootstrap:SuperAdminSetupKey")}==>REDACTED-SETUP-KEY',
    f'literal:{sec("Email:SenderAppPassword")}==>REDACTED-SMTP-PASSWORD',
    # the DB password is a common word, so it is replaced ONLY where it is the Password= of a connection string
    'regex:(?i)(Password=)' + re.escape(dbpw) + r'(?=[;"\'\s\\])==>\1REDACTED',
]
open(REPL, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines) + '\n')
try:
    if os.path.isdir(NEW): shutil.rmtree(NEW, onerror=lambda f, p, e: (os.chmod(p, 0o700), f(p)))
    subprocess.run(['git', 'clone', '-q', '--mirror', ORIG, NEW], check=True)
    g = ['git', f'--git-dir={NEW}']
    # the remote-tracking refs of the working copy become real branches (oct_21 exists only there); then drop the tracking refs and the origin link
    for name in ['oct_21']:
        subprocess.run(g + ['branch', name, f'refs/remotes/origin/{name}'], check=True)
    refs = subprocess.run(g + ['for-each-ref', '--format=%(refname)', 'refs/remotes'], capture_output=True, text=True).stdout.split()
    for r_ in refs: subprocess.run(g + ['update-ref', '--no-deref', '-d', r_], check=True)
    subprocess.run(g + ['remote', 'remove', 'origin'], capture_output=True)
    cmd = ['git', 'filter-repo', '--force', '--invert-paths', '--path', 'Docs/demo-credentials.txt', '--path-glob', 'Docs/smss-db*', '--path', 'smss-api/smss-api/uploads',
           '--replace-text', REPL]
    r = subprocess.run(cmd, cwd=NEW, env=dict(os.environ, GIT_DIR=NEW), capture_output=True, text=True)
    print(r.stdout[-600:], r.stderr[-1500:])
    sys.exit(r.returncode)
finally:
    if os.path.exists(REPL): os.remove(REPL)
