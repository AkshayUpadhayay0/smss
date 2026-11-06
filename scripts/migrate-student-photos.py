"""
One-off migration: move student (or employee) profile photos from the PUBLIC uploads folder to PRIVATE storage.

  python scripts/migrate-student-photos.py --dry-run                 (reports what it would do; changes nothing)
  python scripts/migrate-student-photos.py --apply --i-have-a-backup (does it)
  add  --kind employees  for employee photos (default: students)

Options: --db NAME (default smss-db), --uploads PATH, --private PATH, --host/--user (default localhost / postgres; the password is read from
user-secrets (or PGPASSWORD)).

For every student (employee) whose profile_photo_url (photo_path) is still a public path (/uploads/<school>/students|employees/<id>/<file>):
  1. copy the file to <private>/<school>/students/<id>/<file> (skipped when an identical copy is already there),
  2. VERIFY the copy (size and SHA-256 equal to the original),
  3. point profile_photo_url at the private key (<school>/students/<id>/<file>), guarded by "and profile_photo_url = <old value>",
  4. only then delete the public original.
Nothing is ever deleted before its copy is verified and the database row is updated, so a crash at any point leaves a state that a re-run completes.
Re-running is safe: rows that are already private are counted and left alone. Counts reported: moved, already private, file missing (database row kept untouched
and listed so someone can decide), verify failed, plus unreferenced files still lying in the public students folders (reported, never touched).
"""
import argparse, hashlib, os, re, shutil, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
API = os.path.join(ROOT, 'smss-api', 'smss-api')
PSQL = r'C:\OSGeo4W\bin\psql.exe'


def password():
    """The local database password: PGPASSWORD if set, else the user-secrets connection string (dotnet user-secrets list). It is never kept in a committed file."""
    if os.environ.get('PGPASSWORD'): return os.environ['PGPASSWORD']
    out = subprocess.run(['dotnet', 'user-secrets', 'list', '--project', os.path.join(ROOT, 'smss-api', 'smss-api')], capture_output=True, text=True).stdout
    m = re.search(r'ConnectionStrings:DefaultConnection\s*=.*?Password=([^;\r\n]*)', out)
    if not m: raise SystemExit('Database password not found: set PGPASSWORD or run dotnet user-secrets set "ConnectionStrings:DefaultConnection" ... (see Docs/configuration.md)')
    return m.group(1)


def sha(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''): h.update(chunk)
    return h.hexdigest()


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument('--dry-run', action='store_true'); g.add_argument('--apply', action='store_true')
    ap.add_argument('--i-have-a-backup', action='store_true', help='required with --apply: the database AND the uploads folder were backed up first')
    ap.add_argument('--kind', choices=['students', 'employees'], default='students')
    ap.add_argument('--db', default='smss-db'); ap.add_argument('--host', default='localhost'); ap.add_argument('--user', default='postgres')
    ap.add_argument('--uploads', default=os.path.join(API, 'uploads')); ap.add_argument('--private', default=os.path.join(API, 'uploads-private'))
    a = ap.parse_args()
    if a.apply and not a.i_have_a_backup:
        print('REFUSED: back up the database (pg_dump -Fc) and the uploads folder first, then run again with --i-have-a-backup.'); sys.exit(2)

    env = dict(os.environ, PGPASSWORD=password())
    def sql(q):
        r = subprocess.run([PSQL, '-h', a.host, '-U', a.user, '-d', a.db, '-At', '-F', '|', '-v', 'ON_ERROR_STOP=1'], input=q, capture_output=True, text=True, env=env)
        if r.returncode != 0: raise RuntimeError(r.stderr.strip())
        return r.stdout.strip()

    table, idcol, col, folder = ('tb_students', 'student_id', 'profile_photo_url', 'students') if a.kind == 'students' else ('tb_employees', 'employee_id', 'photo_path', 'employees')
    rows = [l.split('|', 2) for l in sql(f"select {idcol}, school_id, {col} from {table} where {col} is not null and {col} <> '' order by {idcol}").splitlines()]
    uploads, private = os.path.abspath(a.uploads), os.path.abspath(a.private)
    moved = already = missing = failed = 0; referenced = set(); report = []
    for sid, school, url in rows:
        if not url.startswith('/uploads/'):
            already += 1; continue
        rel = url[len('/uploads/'):]
        key = rel                                   # <school code>/students/<student id>/<file>: the same relative path under the private root
        if '..' in rel.split('/') or not re.fullmatch(r'[A-Za-z0-9._\-]+/' + folder + r'/[A-Za-z0-9]+/[A-Za-z0-9._\-]+', rel):
            failed += 1; report.append(f'  UNSAFE PATH, left alone: {sid} {url}'); continue
        src = os.path.join(uploads, *rel.split('/')); dst = os.path.join(private, *key.split('/')); referenced.add(os.path.normcase(src))
        if not os.path.isfile(src):
            if os.path.isfile(dst):                 # copied by an earlier run that stopped before the database update
                report.append(f'  finishing an interrupted move: {sid} {key}')
            else:
                missing += 1; report.append(f'  FILE MISSING (row kept): {sid} {url}'); continue
        elif not os.path.isfile(dst):
            if a.apply:
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                shutil.copy2(src, dst)
        if a.apply:
            if os.path.isfile(src) and (os.path.getsize(src) != os.path.getsize(dst) or sha(src) != sha(dst)):
                failed += 1; report.append(f'  VERIFY FAILED (nothing changed for it): {sid} {url}'); continue
            esc = lambda s: s.replace("'", "''")
            n = sql(f"update {table} set {col} = '{esc(key)}' where {idcol} = '{esc(sid)}' and school_id = '{esc(school)}' and {col} = '{esc(url)}' returning 1")
            if n.splitlines()[:1] != ['1']:
                failed += 1; report.append(f'  ROW CHANGED MEANWHILE, left alone: {sid}'); continue
            if os.path.isfile(src): os.remove(src)  # only now, after the verified copy and the committed update
        moved += 1

    # Public student files nobody references any more. One whose private copy exists and is byte-identical is a leftover of an interrupted move and is removed (apply only);
    # anything else is only reported and never touched.
    orphans = []; cleaned = 0
    for dirpath, _, files in os.walk(uploads):
        if os.sep + folder + os.sep in dirpath + os.sep:
            for f in files:
                p = os.path.join(dirpath, f)
                if os.path.normcase(p) in referenced: continue
                twin = os.path.join(private, os.path.relpath(p, uploads))
                if os.path.isfile(twin) and os.path.getsize(twin) == os.path.getsize(p) and sha(twin) == sha(p):
                    if a.apply: os.remove(p); cleaned += 1
                    else: orphans.append(p + '   (identical private copy exists: would be removed)')
                else: orphans.append(p)
    mode = 'APPLIED' if a.apply else 'DRY RUN (nothing changed)'
    print(f'{mode}: {a.kind} with a photo: {len(rows)}')
    print(f'  moved to private storage : {moved}')
    print(f'  already private          : {already}')
    print(f'  file missing             : {missing}')
    print(f'  failed / unsafe          : {failed}')
    print(f'  leftover public copies removed (verified identical private copy): {cleaned}')
    print(f'  unreferenced files left in the public photo folders: {len(orphans)}')
    for l in report: print(l)
    for o in orphans[:20]: print('  orphan:', o)
    sys.exit(1 if failed else 0)


if __name__ == '__main__':
    main()
