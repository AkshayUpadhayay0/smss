"""Helpers for the manual-test setup. Reads the DB password from user-secrets (never printed). Only ever talks to smss-db-dev."""
import json, os, re, subprocess, sys
PSQL = r'C:\Program Files\PostgreSQL\18\bin\psql.exe'
DB = 'smss-db-dev'
SECRETS = os.path.expandvars(r'%APPDATA%\Microsoft\UserSecrets\smss-api-d747c89c-f5c3-40e1-ad3f-9ea08a949b1b\secrets.json')


def conn_parts():
    cs = json.load(open(SECRETS, encoding='utf-8-sig'))['ConnectionStrings:DefaultConnection']
    kv = dict(p.split('=', 1) for p in cs.split(';') if '=' in p)
    return kv


def dev_connection_string():
    """Connection string pointing at smss-db-dev (value stays in memory / child env only)."""
    kv = conn_parts()
    kv['Database'] = DB
    return ';'.join(f'{k}={v}' for k, v in kv.items())


def psql(sql, db=DB, tuples=True):
    assert db == DB, 'refusing to touch any database except smss-db-dev'
    kv = conn_parts()
    env = dict(os.environ, PGPASSWORD=kv['Password'])
    args = [PSQL, '-h', kv.get('Host', 'localhost'), '-p', kv.get('Port', '5432'), '-U', kv['Username'], '-d', db, '-v', 'ON_ERROR_STOP=1']
    if tuples:
        args += ['-At']
    r = subprocess.run(args + ['-c', sql], capture_output=True, text=True, env=env)
    if r.returncode:
        raise RuntimeError(r.stderr.strip()[:500])
    return r.stdout


def fingerprint():
    q = ("select table_name||'='||(xpath('/row/c/text()', query_to_xml(format('select count(*) as c from public.%I', table_name), false, true, '')))[1]::text "
         "from information_schema.tables where table_schema='public' and table_type='BASE TABLE' order by 1")
    return psql(q)


if __name__ == '__main__':
    cmd = sys.argv[1]
    if cmd == 'fp':
        sys.stdout.write(fingerprint())
    elif cmd == 'sql':
        sys.stdout.write(psql(sys.argv[2]))
