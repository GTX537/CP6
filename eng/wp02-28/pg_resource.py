"""Own WP02-28 PostgreSQL resource; never reads application or prior-task credentials.

create prints only the settings path. verify prints safe identity. stop uses smart
shutdown only after identity and zero-client checks. Data/logs are always retained.
Adapted from the reviewed WP02-07 helper; explicit checks also apply under python -O.
"""
import datetime
import json
import os
from pathlib import Path
import re
import secrets
import socket
import subprocess
import sys
import time

BIN = Path('C:/Program Files/PostgreSQL/18/bin')
ROOT = Path('D:/CP6-local-test/WP02-28')
PORT = 55438
NO_WINDOW = 0x08000000 if os.name == 'nt' else 0


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def run(args, *, env=None, data=None):
    p = subprocess.run([str(x) for x in args], input=data, capture_output=True,
                       text=True, encoding='utf-8', env=env, creationflags=NO_WINDOW)
    if p.returncode:
        # Never echo arguments, SQL, credentials, or provider diagnostics.
        raise RuntimeError(f'{Path(str(args[0])).name} failed ({p.returncode})')
    return p.stdout.strip()


def env_for(c):
    e = {k: v for k, v in os.environ.items() if not k.upper().startswith('PG')}
    e.update(PGPASSFILE=c['PassFile'], PGCONNECT_TIMEOUT='5', PGSSLMODE='disable')
    return e


def sql(c, query, database=None, runtime=False):
    return run([BIN/'psql.exe', '-X', '-w', '-h', '127.0.0.1', '-p', PORT,
                '-U', c['Runtime'] if runtime else c['Admin'], '-d', database or c['Database'],
                '-At', '-v', 'ON_ERROR_STOP=1'], env=env_for(c), data=query)


def validate_paths(c):
    require(ROOT.resolve() == ROOT, 'Task root redirects outside its declared path')
    owner = c['Owner']
    require(re.fullmatch('[0-9a-f]{32}', owner) is not None, 'Invalid owner token')
    directory = ROOT/owner
    require(directory.resolve() == directory, 'Run path redirects')
    for key, expected in [('RunDirectory', directory), ('DataDirectory', directory/'data'),
                          ('PassFile', directory/'pgpass')]:
        require(Path(c[key]).resolve() == expected, 'Private path mismatch')
    require(c['Port'] == PORT and c['Marker'] == 'CP6-WP02-28:'+owner, 'Task identity mismatch')
    require(c['Admin'] == 'wp0228_admin_'+owner[:8] and
            c['Runtime'] == 'wp0228_runtime_'+owner[:8], 'Principal mismatch')
    require(re.fullmatch('cp6_wp0228_[0-9]{8}_'+owner[:8], c['Database']) is not None,
            'Database name mismatch')
    marker = json.loads((directory/'owner-marker.json').read_text(encoding='utf-8'))
    require(marker == {'Owner': owner, 'Marker': c['Marker'], 'Port': PORT}, 'Private marker mismatch')
    return directory


def load_settings(path):
    path = Path(path).absolute()
    require(path.resolve() == path and path.name == 'settings.json' and
            path.parent.parent == ROOT and re.fullmatch('[0-9a-f]{32}', path.parent.name),
            'Settings path is not a contained task-owned path')
    c = json.loads(path.read_text(encoding='utf-8'))
    require(validate_paths(c) == path.parent, 'Settings owner mismatch')
    return c


def identity(c, database=None):
    validate_paths(c)
    result = json.loads(sql(c, "SELECT json_build_object('SystemIdentifier',system_identifier::text,"
               "'DataDirectory',current_setting('data_directory'),'Port',inet_server_port(),"
               "'Database',current_database(),'Oid',(SELECT oid FROM pg_database WHERE datname=current_database()),"
               "'DatabaseOwner',(SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname=current_database()),"
               "'Principal',session_user) FROM pg_control_system();", database))
    require(Path(result['DataDirectory']).resolve() == Path(c['DataDirectory']).resolve(), 'Server path mismatch')
    require(result['Port'] == PORT and result['Principal'] == c['Admin'], 'Server endpoint/principal mismatch')
    require(result['Database'] == (database or c['Database']) and result['DatabaseOwner'] == c['Admin'],
            'Database identity/owner mismatch')
    require(result['SystemIdentifier'] == c['SystemIdentifier'], 'Server system identifier mismatch')
    if not database and 'DatabaseOid' in c:
        require(result['Oid'] == c['DatabaseOid'], 'Database OID mismatch')
    return result


def zero_sessions(c, database=None):
    require(sql(c, "SELECT count(*) FROM pg_stat_activity WHERE backend_type='client backend' "
                "AND pid<>pg_backend_pid();", database) == '0',
            'Unexpected live sessions; no termination authorized')


def runtime_identity(c):
    result = json.loads(sql(c, "SELECT json_build_object('Principal',session_user,'Database',current_database(),"
              "'Port',inet_server_port(),'Owner',r.owner,'Marker',r.marker,'Superuser',u.rolsuper,"
              "'CreateDatabase',u.rolcreatedb,'CreateRole',u.rolcreaterole,'Inherit',u.rolinherit,"
              "'BypassRls',u.rolbypassrls,'CreateSchema',has_database_privilege(current_database(),'CREATE'),"
              "'CreateFixture',has_schema_privilege('fixture','CREATE')) "
              "FROM wp0228_guard.resource r CROSS JOIN pg_roles u WHERE u.rolname=session_user;", runtime=True))
    require(result['Principal'] == c['Runtime'] and result['Database'] == c['Database'] and
            result['Port'] == PORT and result['Owner'] == c['Owner'] and result['Marker'] == c['Marker'],
            'Runtime identity mismatch')
    require(not any(result[k] for k in ('Superuser', 'CreateDatabase', 'CreateRole', 'Inherit',
                                      'BypassRls', 'CreateSchema', 'CreateFixture')),
            'Runtime has unauthorized privilege')
    return result


def verify(c):
    actual = identity(c)
    require(sql(c, "SELECT owner || ':' || marker FROM wp0228_guard.resource;") == c['Owner']+':'+c['Marker'],
            'Database marker mismatch')
    actual['RuntimeIdentity'] = runtime_identity(c)
    zero_sessions(c)
    actual.update(Owner=c['Owner'], Marker=c['Marker'], RunDirectory=c['RunDirectory'])
    return actual


def create(owner):
    require(ROOT.resolve() == ROOT, 'Task root redirects outside its declared path')
    with socket.socket() as probe:
        if os.name == 'nt':
            probe.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        probe.bind(('127.0.0.1', PORT))
    require(re.fullmatch("[0-9a-f]{32}", owner) is not None, "Explicit owner required")
    directory = ROOT/owner
    require(not directory.exists() and directory.resolve() == directory, 'Run directory is not fresh')
    directory.mkdir(parents=True)
    user = run(['whoami'])
    run(['icacls', directory, '/inheritance:r', '/grant:r', f'{user}:(OI)(CI)F', 'SYSTEM:(OI)(CI)F'])
    admin_pass, runtime_pass = secrets.token_hex(24), secrets.token_hex(24)
    c = dict(Owner=owner, Marker='CP6-WP02-28:'+owner, Port=PORT,
             Database='cp6_wp0228_'+datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d')+'_'+owner[:8],
             Admin='wp0228_admin_'+owner[:8], Runtime='wp0228_runtime_'+owner[:8],
             RunDirectory=str(directory), DataDirectory=str(directory/'data'), PassFile=str(directory/'pgpass'),
             AdminPassword=admin_pass, RuntimePassword=runtime_pass)
    (directory/'owner-marker.json').write_text(json.dumps({k: c[k] for k in ('Owner','Marker','Port')}), encoding='utf-8')
    validate_paths(c)
    pwfile = directory/'init-password'
    pwfile.write_text(admin_pass+'\n', encoding='utf-8')
    Path(c['PassFile']).write_text(f'127.0.0.1:{PORT}:*:{c["Admin"]}:{admin_pass}\n'
                                 f'127.0.0.1:{PORT}:{c["Database"]}:{c["Runtime"]}:{runtime_pass}\n', encoding='utf-8')
    log = run([BIN/'initdb.exe', '-D', c['DataDirectory'], '-U', c['Admin'], '--pwfile='+str(pwfile),
               '--auth-host=scram-sha-256', '--auth-local=scram-sha-256', '--encoding=UTF8', '--locale=C'])
    (directory/'init.log').write_text(log, encoding='utf-8')
    with (directory/'data'/'postgresql.conf').open('a', encoding='utf-8') as f:
        f.write(f"\nlisten_addresses='127.0.0.1'\nport={PORT}\npassword_encryption='scram-sha-256'\n")
    control = run([BIN/'pg_controldata.exe', '-D', c['DataDirectory']])
    c['SystemIdentifier'] = next(line.split(':', 1)[1].strip() for line in control.splitlines()
                                if line.startswith('Database system identifier:'))
    (directory/'settings-before-start.json').write_text(json.dumps(c, indent=2), encoding='utf-8')
    validate_paths(c)
    quote = lambda value: "'" + str(value).replace("'", "''") + "'"
    args = ','.join(quote(x) for x in ['-D', c['DataDirectory'], '-l', directory/'server.log', '-w', 'start'])
    script = (f"Start-Process -FilePath {quote(BIN/'pg_ctl.exe')} -ArgumentList @({args}) -WindowStyle Hidden "
              f"-RedirectStandardOutput {quote(directory/'start.stdout.log')} -RedirectStandardError {quote(directory/'start.stderr.log')}")
    # No capture/wait: descendants can retain captured Windows pipes. Verified identity proves readiness.
    with (directory/'launcher.log').open('w', encoding='utf-8') as launcher_log:
        subprocess.Popen(['powershell.exe', '-NoProfile', '-NonInteractive', '-Command', script],
                         stdin=subprocess.DEVNULL, stdout=launcher_log, stderr=launcher_log,
                         creationflags=NO_WINDOW)
    for attempt in range(100):
        try:
            identity(c, 'postgres')
            break
        except RuntimeError:
            if attempt == 99:
                raise
            time.sleep(.2)
    zero_sessions(c, 'postgres')
    sql(c, f'CREATE ROLE "{c["Runtime"]}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS PASSWORD \'{runtime_pass}\';\n'
           f'CREATE DATABASE "{c["Database"]}" OWNER "{c["Admin"]}";', 'postgres')
    c['DatabaseOid'] = identity(c)['Oid']
    zero_sessions(c)
    sql(c, f'REVOKE ALL ON DATABASE "{c["Database"]}" FROM PUBLIC;'
           f'GRANT CONNECT ON DATABASE "{c["Database"]}" TO "{c["Runtime"]}";'
           'REVOKE CREATE ON SCHEMA public FROM PUBLIC; CREATE SCHEMA wp0228_guard; CREATE SCHEMA fixture;'
           'CREATE TABLE wp0228_guard.resource(owner text PRIMARY KEY, marker text NOT NULL);'
           f"INSERT INTO wp0228_guard.resource VALUES ('{owner}','{c['Marker']}');"
           f'GRANT USAGE ON SCHEMA wp0228_guard, fixture TO "{c["Runtime"]}";'
           f'GRANT SELECT ON wp0228_guard.resource TO "{c["Runtime"]}";'
           f'ALTER DEFAULT PRIVILEGES IN SCHEMA fixture GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO "{c["Runtime"]}";'
           f'ALTER DEFAULT PRIVILEGES IN SCHEMA fixture GRANT USAGE, SELECT ON SEQUENCES TO "{c["Runtime"]}";')
    actual = verify(c)
    settings = directory/'settings.json'
    settings.write_text(json.dumps(c, indent=2), encoding='utf-8')
    (directory/'ownership.json').write_text(json.dumps(actual, indent=2), encoding='utf-8')
    print(settings)


def stop(c):
    actual = verify(c)
    run([BIN/'pg_ctl.exe', '-D', c['DataDirectory'], '-m', 'smart', '-w', '-t', '15', 'stop'])
    (Path(c['RunDirectory'])/'stopped.json').write_text(json.dumps(actual, indent=2), encoding='utf-8')
    print('Verified owned server stopped; data and logs retained; no files deleted.')


def main():
    require(len(sys.argv) >= 2, 'Expected create <owner32>, verify <settings.json> or stop <settings.json>')
    if sys.argv[1] == 'create' and len(sys.argv) == 3:
        create(sys.argv[2])
    elif sys.argv[1] in ('verify', 'stop') and len(sys.argv) == 3:
        c = load_settings(sys.argv[2])
        if sys.argv[1] == 'verify':
            print(json.dumps(verify(c), indent=2))
        else:
            stop(c)
    else:
        raise ValueError('Expected create <owner32>, verify <settings.json> or stop <settings.json>')


if __name__ == '__main__':
    main()
