#!/usr/bin/env python3
"""Verify ordinary DELETE isolation and the native key-column ACL on a private PG copy."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from uuid import uuid4

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'rma-tenant-archive'))
import archive

SCHEMA = 'cp6_pgt008_delete'
TABLE = f'{SCHEMA}."SharedMessage"'
KEYS = 'public."DataProtectionKeys"'
HELPER = 'cp6_tenancy_v1.current_tenant_id()'
PREDICATE = f'"TenantId" = {HELPER}'
Q = archive.literal
SQLSTATE = re.compile(rb'\bERROR:\s+([0-9A-Z]{5}):')
PHYSICAL_SQL = """
    SELECT jsonb_build_object('target',jsonb_build_object('Server',host(inet_server_addr()),
        'Port',inet_server_port(),'Database',d.datname,'DatabaseOid',d.oid::bigint,
        'SystemIdentifier',p.system_identifier::text),'data',current_setting('data_directory'),
        'owner',pg_get_userbyid(d.datdba),'session',session_user,'current',current_user,
        'super',current_setting('is_superuser')='on','version',current_setting('server_version'),
        'recovery',pg_is_in_recovery())
    FROM pg_database d CROSS JOIN pg_control_system() p WHERE d.datname=current_database();
"""


def require(value, error):
    if not value:
        raise ValueError(error)


def ident(value):
    return '"' + value.replace('"', '""') + '"'


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


class Native:
    def __init__(self, pg_bin, output, target, owner, passwords):
        self.pg_bin, self.output, self.target, self.owner = pg_bin, output, target, owner
        self.passwords = passwords

    def sql(self, label, sql, *, role=None, tags=False, state=None):
        role = role or self.owner
        env = archive.client_environment({key:value for key,value in os.environ.items()
                                          if not key.upper().startswith('CP6_W427_')}, 'cp6-w427-' + label)
        env.update(PGPASSFILE=str(self.output / 'absent-password-file'), PGPASSWORD=self.passwords[role])
        args = [archive.binary(self.pg_bin, 'psql'), '-XAtw', '-v', 'ON_ERROR_STOP=1', '-v', 'VERBOSITY=verbose',
                *archive.connection_args(self.target, role)]
        if not tags:
            args.append('-q')
        wire = "SET standard_conforming_strings=on;\nSET search_path=pg_catalog;\n" + sql + '\n'
        (self.output / (label + '.sql')).write_text(wire, encoding='utf-8')
        result = subprocess.run(args, input=wire.encode('utf-8'), capture_output=True,
                                env=env, timeout=20, creationflags=archive.FLAGS)
        (self.output / (label + '.stdout')).write_bytes(result.stdout)
        (self.output / (label + '.stderr')).write_bytes(result.stderr)
        states = [value.decode('ascii') for value in SQLSTATE.findall(result.stderr)]
        save(self.output / (label + '.json'), dict(role=role, exit=result.returncode, sqlstates=states))
        if state is None:
            require(result.returncode == 0 and not result.stderr, label + ' unexpected native failure')
        else:
            require(result.returncode == 3 and states == [state] and not result.stdout,
                    label + ' expected SQLSTATE ' + state + ' and empty stdout')
        return result

    def read(self, label, sql, *, role=None):
        result = self.sql(label, 'BEGIN READ ONLY;\n' + sql + '\nCOMMIT;', role=role)
        return json.loads(result.stdout)


def setup(db, tenants, logins, ids):
    a, b = logins
    roles = f'{ident(a)}, {ident(b)}'
    rows = ', '.join(f'({Q(tenant)},{Q(key)},{Q("pgt008-" + label)})'
                     for tenant, key, label in zip(tenants, ids, ('a', 'b')))
    db.sql('test-shared-table', f"""
        BEGIN;
        CREATE SCHEMA {SCHEMA};
        REVOKE ALL ON SCHEMA {SCHEMA} FROM PUBLIC;
        CREATE TABLE {TABLE} ("TenantId" uuid NOT NULL, "Id" uuid PRIMARY KEY, "Payload" text NOT NULL);
        REVOKE ALL ON TABLE {TABLE} FROM PUBLIC;
        INSERT INTO {TABLE} ("TenantId","Id","Payload") VALUES {rows};
        ALTER TABLE {TABLE} ENABLE ROW LEVEL SECURITY;
        ALTER TABLE {TABLE} FORCE ROW LEVEL SECURITY;
        CREATE POLICY tenant_select_v1 ON {TABLE} FOR SELECT TO {roles} USING ({PREDICATE});
        CREATE POLICY tenant_insert_v1 ON {TABLE} FOR INSERT TO {roles} WITH CHECK ({PREDICATE});
        CREATE POLICY tenant_update_v1 ON {TABLE} FOR UPDATE TO {roles} USING ({PREDICATE}) WITH CHECK ({PREDICATE});
        CREATE POLICY tenant_delete_v1 ON {TABLE} FOR DELETE TO {roles} USING ({PREDICATE});
        GRANT USAGE ON SCHEMA {SCHEMA} TO {roles};
        GRANT SELECT ("Id") ON {TABLE} TO {roles};
        GRANT DELETE ON {TABLE} TO {ident(a)};
        COMMIT;
    """)


def catalog(db, logins):
    roles = ','.join(map(Q, logins))
    return db.read('native-catalog', f"""
        WITH test AS (SELECT * FROM pg_class WHERE oid={Q(TABLE)}::regclass),
             ordinary AS (SELECT * FROM pg_roles WHERE rolname IN ({roles})),
             key_table AS (SELECT * FROM pg_class WHERE oid={Q(KEYS)}::regclass),
             key_column AS (SELECT * FROM pg_attribute WHERE attrelid={Q(KEYS)}::regclass
                            AND attname='Xml' AND attnum>0 AND NOT attisdropped),
             helper AS (SELECT * FROM pg_proc WHERE oid={Q(HELPER)}::regprocedure)
        SELECT jsonb_build_object('table',(SELECT jsonb_build_object('oid',oid::bigint,'owner',pg_get_userbyid(relowner),
              'rls',relrowsecurity,'force',relforcerowsecurity,'acl',relacl) FROM test),
            'policies',(SELECT jsonb_object_agg(polname,jsonb_build_object('command',polcmd,'roles',
                 ARRAY(SELECT value::bigint FROM unnest(polroles) value ORDER BY value),
                 'permissive',polpermissive,'using',pg_get_expr(polqual,polrelid),'check',pg_get_expr(polwithcheck,polrelid)))
                 FROM pg_policy WHERE polrelid={Q(TABLE)}::regclass),
            'roles',(SELECT jsonb_object_agg(r.rolname,jsonb_build_object('oid',r.oid::bigint,'login',r.rolcanlogin,
                 'super',r.rolsuper,'inherit',r.rolinherit,'bypassRls',r.rolbypassrls,'createDb',r.rolcreatedb,
                 'createRole',r.rolcreaterole,'replication',r.rolreplication,'connectionLimit',r.rolconnlimit,
                 'validUntil',r.rolvaliduntil,'memberships',(SELECT count(*) FROM pg_auth_members
                     WHERE member=r.oid OR roleid=r.oid),'settings',(SELECT count(*) FROM pg_db_role_setting WHERE setrole=r.oid),
                 'tablePrivileges',ARRAY(SELECT privilege FROM unnest(ARRAY['SELECT','INSERT','UPDATE','DELETE','TRUNCATE',
                     'REFERENCES','TRIGGER','MAINTAIN']) privilege WHERE has_table_privilege(r.oid,{Q(TABLE)}::regclass,privilege)),
                 'selectColumns',ARRAY(SELECT attname FROM pg_attribute WHERE attrelid={Q(TABLE)}::regclass AND attnum>0
                     AND has_column_privilege(r.oid,attrelid,attnum,'SELECT') ORDER BY attnum),
                 'publicUsage',has_schema_privilege(r.oid,'public','USAGE'),
                 'keyTableSelect',has_table_privilege(r.oid,{Q(KEYS)}::regclass,'SELECT'),
                 'keyXmlSelect',(SELECT has_column_privilege(r.oid,attrelid,attnum,'SELECT') FROM key_column))) FROM ordinary r),
            'key',(SELECT jsonb_build_object('relation',c.oid::bigint,'tableAcl',c.relacl,'columnAcl',a.attacl,
                   'owner',pg_get_userbyid(c.relowner),'column',a.attname,'type',format_type(a.atttypid,a.atttypmod))
                   FROM key_table c JOIN key_column a ON a.attrelid=c.oid),
            'helper',(SELECT jsonb_build_object('owner',pg_get_userbyid(proowner),'acl',proacl,'config',proconfig,
                 'securityDefiner',prosecdef,'definition',pg_get_functiondef(oid)) FROM helper));
    """)


def check_catalog(facts, owner, logins, role_oids):
    require(facts['table']['owner'] == owner and facts['table']['rls'] and facts['table']['force']
            and facts['helper']['securityDefiner'] and facts['key']['column'] == 'Xml', 'wrong actual RLS or key layout')
    expected = {}
    predicate = f'({PREDICATE})'
    for name, action, using, check in (('select','r',predicate,None), ('insert','a',None,predicate),
                                       ('update','w',predicate,predicate), ('delete','d',predicate,None)):
        expected['tenant_' + name + '_v1'] = dict(command=action, roles=sorted(role_oids.values()),
                                                permissive=True, using=using, check=check)
    require(facts['policies'] == expected, 'unexpected native effective policy combination')
    for role in logins:
        actual = facts['roles'][role]
        require(actual['oid'] == role_oids[role] and actual['login'] and actual['publicUsage']
                and not any(actual[name] for name in ('super','bypassRls','createDb','createRole','replication',
                                                     'memberships','settings','keyTableSelect','keyXmlSelect'))
                and actual['tablePrivileges'] == (['DELETE'] if role == logins[0] else [])
                and actual['selectColumns'] == ['Id'], 'unexpected native role or grant: ' + role)


def observe(db, label, table, where=''):
    observed = db.read(label, f"""
        SELECT jsonb_build_object('observer',session_user,'backendPid',pg_backend_pid(),
          'readOnly',current_setting('transaction_read_only'),
          'rows',(SELECT jsonb_object_agg(s."Id",jsonb_build_object('row',to_jsonb(s),
                     'xmin',s.xmin::text,'ctid',s.ctid::text)) FROM {table} s {where}));
    """)
    require(observed['observer'] == db.owner and observed['readOnly'] == 'on', 'observer was not independent read-only')
    return observed


def ordinary_identity():
    return f"""SELECT jsonb_build_object('session',session_user,'current',current_user,'tenant',{HELPER},
                'pid',pg_backend_pid(),'rls',row_security_active({Q(TABLE)}::regclass));"""


def check_login(observed, login, tenant):
    require(observed['session'] == observed['current'] == login and observed['tenant'] == tenant
            and observed['rls'], 'ordinary login or effective RLS was not the expected tenant')


def delete(db, label, login, tenant, count, where=''):
    result = db.sql(label, f'BEGIN;\n{ordinary_identity()}\nDELETE FROM {TABLE}{where};\n\\echo ROW_COUNT :ROW_COUNT\nCOMMIT;',
                    role=login, tags=True)
    lines = result.stdout.splitlines()
    require(lines[-3:] == [f'DELETE {count}'.encode(), f'ROW_COUNT {count}'.encode(), b'COMMIT'],
            label + ' incorrect native command tag or affected count')
    observed = json.loads(lines[-4])
    check_login(observed, login, tenant)
    return observed['pid']


def execute(args, passwords, checkpoint=lambda label: None):
    copy = json.loads(args.copy_receipt.read_bytes())
    source, data = (Path(copy[key]) for key in ('Source','Copy'))
    require(args.output.name not in ('', '.', '..'), 'new evidence directory required')
    output = args.output.parent.resolve(strict=True) / args.output.name
    require(all(path.resolve(strict=True) == path for path in (source,data)) and source != data
            and source not in data.parents and data not in source.parents
            and not any(path in (output, *output.parents) for path in (source,data))
            and not (source/'postmaster.pid').exists(), 'exact isolated private copy is required')
    physical = archive.PhysicalTarget.from_mapping(copy['Target'])
    owner = copy['Owner']
    tenants = [str(archive.guid(binding['TenantId'])) for binding in copy['Tenants']]
    logins = [binding['Login'] for binding in copy['Tenants']]
    require(len(set(tenants)) == 2 and len(set([owner,*logins])) == 3 and all(passwords.get(role) for role in (owner,*logins)),
            'two distinct ordinary SCRAM logins and the control credential are required')
    require(archive.file_digest(args.stock_witness) == copy['StockWitness'], 'wrong prior Stock observation')
    prior = json.loads(args.stock_witness.read_bytes())['rows']
    archive.private_directory(output)
    db = Native(args.pg_bin, output, physical, owner, passwords)
    checkpoint('before-setup')
    target = db.read('physical-copy', PHYSICAL_SQL)
    require(target['target'] == physical.mapping() and Path(target['data']).resolve(strict=True) == data
            and target['owner'] == target['session'] == target['current'] == owner and target['super']
            and not target['recovery'], 'native database is outside the private copy')
    scope = ','.join(Q(tenant) + '::uuid' for tenant in tenants)
    stock = ('public."T_WmsSequence"', f'WHERE s."TenantId" IN ({scope})')
    require(observe(db, 'stock-before', *stock)['rows'] == prior, 'cloned Stock rows differ from original W368 witness')
    ids = [str(uuid4()), str(uuid4())]
    setup(db, tenants, logins, ids)
    check_catalog(catalog(db, logins), owner, logins, copy['RoleOids'])
    checkpoint('before-delete-cases')
    a, b = ids
    start = observe(db, 'fixture-before', TABLE)
    require(set(start['rows']) == {a,b} and [start['rows'][key]['row']['TenantId'] for key in ids] == tenants,
            'both original fixture tenants must have visible witness rows')
    for login, tenant, key in zip(logins, tenants, ids):
        facts = db.sql('visibility-' + ('a' if key == a else 'b'),
                       ordinary_identity() + f'\nSELECT jsonb_agg("Id" ORDER BY "Id") FROM {TABLE};', role=login)
        identity, visible = map(json.loads, facts.stdout.splitlines())
        check_login(identity, login, tenant)
        require(visible == [key], 'native ordinary visibility differs')
    targeted = delete(db, 'targeted-b-delete', logins[0], tenants[0], 0, f' WHERE "Id"={Q(b)}::uuid')
    after_target = observe(db, 'fixture-after-targeted', TABLE)
    require(after_target['rows'] == start['rows'], 'targeted B delete changed a native tuple')
    checkpoint('before-unfiltered-delete')
    unfiltered = delete(db, 'unfiltered-a-delete', logins[0], tenants[0], 1)
    after_delete = observe(db, 'fixture-after-unfiltered', TABLE)
    require(after_delete['rows'] == {b: start['rows'][b]}
            and len({start['backendPid'],after_target['backendPid'],after_delete['backendPid'],targeted,unfiltered}) == 5,
            'expected only the identical B row, witnessed in separate native sessions')
    checkpoint('before-key-projection')
    denial = db.sql('data-protection-xml', f'SELECT "Xml" FROM {KEYS} LIMIT 0;', role=logins[0], state='42501')
    require(b'permission denied' in denial.stderr and b'DataProtectionKeys' in denial.stderr,
            'key rejection did not identify the real table ACL')
    require(observe(db, 'stock-after', *stock)['rows'] == prior, 'original W368 Stock rows changed in the copy')
    result = dict(physical=target, nativeRoles=copy['RoleOids'], tenants=tenants, targetedDelete=0, unfilteredDelete=1,
                  bUnchanged=True, stockUnchanged=True, keyProjectionSqlstate='42501', keyBytesRead=0)
    save(output/'actual.json', result)
    print('Native DELETE 0 for B and unfiltered DELETE 1 for A. Independent B tuple unchanged. Key Xml projection 42501.',
          flush=True)
    return result


def parser():
    cli = argparse.ArgumentParser(description=__doc__)
    for key in ('pg-bin','copy-receipt','stock-witness','output'):
        cli.add_argument('--' + key, required=True, type=Path)
    return cli


if __name__ == '__main__':
    options = parser().parse_args()
    recorded = json.loads(options.copy_receipt.read_bytes())
    roles = [recorded['Owner'], *(value['Login'] for value in recorded['Tenants'])]
    credentials = {role: os.environ.get('CP6_W427_' + key + '_PASSWORD') for role, key in zip(roles, ('OWNER','A','B'))}
    execute(options, credentials)
