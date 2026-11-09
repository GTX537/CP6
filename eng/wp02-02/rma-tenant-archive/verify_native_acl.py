#!/usr/bin/env python3
"""Run PGT-006/007/009 on a private PostgreSQL copy of a closed Core candidate."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from uuid import uuid4

sys.dont_write_bytecode = True
import archive
import candidate_core
import candidate_journal
from verify_core_candidate_journal import sample_state

Q = archive.literal
JOURNAL = archive.JOURNAL_TABLE
HELPER = 'cp6_tenancy_v1.current_tenant_id()'
DENIED = '42501'
SQLSTATE = re.compile(rb'\bERROR:\s+([0-9A-Z]{5}):')


def require(value, message):
    if not value:
        raise ValueError(message)


def ident(value):
    return '"' + value.replace('"', '""') + '"'


class Native:
    def __init__(self, args, target, output):
        self.args, self.target, self.output = args, target, output
        self.a, self.b = (str(args.tenant_a), str(args.tenant_b))
        self.a_schema, self.b_schema = ('t_' + tenant.hex for tenant in (args.tenant_a, args.tenant_b))
        self.passwords = {role: os.environ.get(key) for role, key in (
            (args.owner, 'CP6_W417_OWNER_PASSWORD'), (args.login_a, 'CP6_W417_A_PASSWORD'),
            (args.login_b, 'CP6_W417_B_PASSWORD'))}
        require(args.owner not in (args.login_a, args.login_b) and args.login_a != args.login_b
                and self.a != self.b and self.passwords[args.login_a] and self.passwords[args.login_b],
                'two distinct ordinary login credentials are required')
        self.results = {}

    def sql(self, label, statement, *, role=None, states=(), exit_ok=True):
        role = role or self.args.owner
        environ = {key: value for key, value in os.environ.items()
                   if not key.upper().startswith(('PG', 'CP6_W417_'))}
        if self.passwords[role]:
            environ['PGPASSWORD'] = self.passwords[role]
        command = [archive.binary(self.args.pg_bin, 'psql'), '-XqAtw', '-v', 'ON_ERROR_STOP=1',
                   '-v', 'VERBOSITY=verbose', *archive.connection_args(self.target, role)]
        wire = ('SET standard_conforming_strings=on;\n' + statement + '\n').encode('utf-8')
        result = subprocess.run(command, input=wire, capture_output=True,
                                env=archive.client_environment(environ, 'w417-' + label), timeout=20,
                                creationflags=archive.FLAGS)
        found = [state.decode('ascii') for state in SQLSTATE.findall(result.stderr)]
        (self.output / (label + '.stdout')).write_bytes(result.stdout)
        (self.output / (label + '.stderr')).write_bytes(result.stderr)
        facts = dict(role=role, exit=result.returncode, sqlstates=found)
        (self.output / (label + '.json')).write_text(json.dumps(facts) + '\n', encoding='utf-8')
        self.results[label] = facts
        require((result.returncode == 0) == exit_ok and found == list(states)
                and (states or not result.stderr), label + ' unexpected PostgreSQL result')
        return result.stdout

    def read(self, label, statement, *, role=None):
        return json.loads(self.sql(label, statement, role=role))

    def deny(self, label, statement):
        require(not self.sql(label, 'BEGIN;\n' + statement + '\nROLLBACK;', role=self.args.login_a,
                             states=(DENIED,), exit_ok=False), label + ' unexpected data in denied action')


def guarded_inputs(args):
    copy = json.loads(args.copy_receipt.read_bytes())
    source, target = (Path(copy[key]).resolve(strict=True) for key in ('Source', 'Copy'))
    require(source != target and source not in target.parents and target not in source.parents,
            'a separate task-owned data directory is required')
    output = args.output.parent.resolve(strict=True) / args.output.name
    require(source not in output.parents and target not in output.parents,
            'evidence must be outside PostgreSQL data directories')
    physical = archive.PhysicalTarget.from_mapping(copy['Target'])
    for key, path in (('PriorState', args.prior_state), ('JournalReceipt', args.journal_receipt)):
        require(archive.file_digest(path) == copy[key], 'copied candidate input digest mismatch for ' + key)
    prior = json.loads(args.prior_state.read_bytes())
    journal = json.loads(args.journal_receipt.read_bytes())
    require(prior['admission'] == 0 and journal['outcome'] == 'IMPORTED' and journal['recoveryReady'] is False
            and journal['binding']['target'] == physical.mapping()
            and journal['binding']['candidateId'] == copy['CandidateId']
            and journal['binding']['tenantId'] == str(args.tenant_a), 'wrong closed candidate receipt')
    archive.private_directory(output)
    db = Native(args, physical, output)
    guard = db.read('physical-and-closed', """
        SELECT jsonb_build_object('target',jsonb_build_object('Server',host(inet_server_addr()),
              'Port',inet_server_port(),'Database',d.datname,'DatabaseOid',d.oid::bigint,
              'SystemIdentifier',p.system_identifier::text),'data',current_setting('data_directory'),
              'owner',pg_get_userbyid(d.datdba),'session',session_user,'current',current_user,
              'super',(SELECT rolsuper FROM pg_roles WHERE rolname=session_user),
              'marker',shobj_description(d.oid,'pg_database'),
              'admission',(SELECT count(*) FROM cp6_privacy_disposition_v1.restore_admission),
              'otherClients',(SELECT count(*) FROM pg_stat_activity WHERE datid=d.oid
                  AND pid<>pg_backend_pid() AND backend_type='client backend'))
        FROM pg_database d CROSS JOIN pg_control_system() p WHERE d.datname=current_database();
    """)
    require(guard == dict(target=physical.mapping(), data=target.as_posix(), owner=args.owner, session=args.owner,
                          current=args.owner, super=True, marker=candidate_core.MARKER + copy['CandidateId'],
                          admission=0, otherClients=0), 'actual active database is outside the closed copy')
    actual = db.sql('original-journal-receipt',
                    f'SELECT result_json FROM {candidate_core.RECEIPTS}.{candidate_journal.RECEIPT};')
    require(actual.rstrip(b'\r\n') == args.journal_receipt.read_bytes().rstrip(b'\r\n'), 'original receipt differs')
    return db, prior


def originals(db, label, *, only_a=False):
    where = f'WHERE tenant_id={Q(db.a)}::uuid AND owner={Q(archive.JOURNAL_OWNER)}' if only_a else ''
    order = 'operation_id' if only_a else 'tenant_id,owner,contract,operation_id'
    return db.sql(label, f"""
        SET timezone='UTC'; SET datestyle='ISO, YMD'; SET bytea_output='hex';
        COPY (SELECT {', '.join(archive.JOURNAL_COLUMNS)} FROM {JOURNAL} {where} ORDER BY {order})
          TO STDOUT WITH (FORMAT csv,HEADER);
    """)


def observed_journal(db, shared_key, *, status='NULL'):
    return f"""
        SELECT jsonb_build_object('attemptSqlstate',{status},'session',session_user,'current',current_user,
          'helper',{HELPER},'unqualified',current_tenant_id(),'guc',current_setting('app.tenant_id',true),
          'path',current_setting('search_path'),
          'rows',(SELECT coalesce(jsonb_agg(jsonb_build_array(tenant_id,operation_id,state,
              encode(sha256(result_bytes),'hex')) ORDER BY operation_id),'[]'::jsonb) FROM {JOURNAL}),
          'bKey',(SELECT count(operation_id) FROM {JOURNAL}
              WHERE tenant_id={Q(db.b)}::uuid AND operation_id={Q(shared_key)}::uuid));
    """


def ordinary_read(db, label, role, shared_key):
    return db.read(label, 'BEGIN; SET LOCAL search_path=pg_catalog,cp6_tenancy_v1,pg_temp;\n'
                   + observed_journal(db, shared_key) + '\nROLLBACK;', role=role)


def expected_journal(db):
    return db.read('owner-visible-journal-rows', f"""
        SELECT jsonb_object_agg(t.tenant_id,t.rows) FROM (
          SELECT tenant_id,jsonb_agg(jsonb_build_array(tenant_id,operation_id,state,
              encode(sha256(result_bytes),'hex')) ORDER BY operation_id) AS rows
          FROM {JOURNAL} WHERE owner={Q(archive.JOURNAL_OWNER)} AND contract={Q(archive.JOURNAL_CONTRACT)}
            AND tenant_id IN ({Q(db.a)}::uuid,{Q(db.b)}::uuid) GROUP BY tenant_id
        ) t;
    """)


def assert_observed(value, login, tenant, expected_rows, b_count):
    require(value['session'] == value['current'] == login and value['helper'] == value['unqualified'] == tenant
            and value['rows'] == expected_rows and value['bKey'] == b_count, 'ordinary session or visible original rows differ')


def check_cross_schema(db, before, sequence):
    expected = db.read('b-native-owned-rows', f"""
        SELECT jsonb_build_object('session',session_user,'current',current_user,'helper',{HELPER},
          'heads',(SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM {db.b_schema}.heads r),
          'versions',(SELECT jsonb_agg(to_jsonb(r) ORDER BY id,version) FROM {db.b_schema}.versions r));
    """, role=db.args.login_b)
    require(expected == dict(session=db.args.login_b, current=db.args.login_b, helper=db.b,
                             heads=before['bHeads'], versions=before['bVersions']), 'B genuine table access did not match owner')
    head, = before['bHeads']
    version, = before['bVersions']
    native_id = Q(head['id'])
    probe_id = Q(str(uuid4()))
    for table, columns, values, assignment in (
        ('heads', 'tenant_id,id,actor_id,outbound_id,version',
         f"{Q(db.b)},{probe_id},{Q(head['actor_id'])},{Q(head['outbound_id'])},{head['version']}", 'version=version'),
        ('versions', 'tenant_id,id,version,state,payload',
         f"{Q(db.b)},{native_id},{version['version']+1},{Q(version['state'])},{Q(json.dumps(version['payload']))}::jsonb",
         'state=state'),
    ):
        target = db.b_schema + '.' + table
        for action, sql in (
            ('select', f'SELECT id FROM {target} WHERE id={native_id};'),
            ('insert', f'INSERT INTO {target}({columns}) VALUES ({values});'),
            ('update', f'UPDATE {target} SET {assignment} WHERE id={native_id};'),
            ('delete', f'DELETE FROM {target} WHERE id={native_id};'),
        ):
            db.deny('006-b-' + table + '-' + action, sql)
    db.deny('006-b-sequence-nextval', f'SELECT nextval({Q(sequence)}::regclass);')


def check_spoof(db, shared_key, rows):
    login = db.args.login_a
    b_id = ident(db.args.login_b)
    update = f"UPDATE {JOURNAL} SET state='CLOSED' WHERE tenant_id={Q(db.b)}::uuid AND operation_id={Q(shared_key)}::uuid;"
    for label, statement in (('set-role', f'SET ROLE {b_id};'), ('set-session-authorization',
                                                                 f'SET SESSION AUTHORIZATION {b_id};')):
        raw = db.sql('007-' + label, rf"""
            BEGIN; SET LOCAL search_path=pg_catalog,cp6_tenancy_v1,pg_temp;
            SAVEPOINT spoof;
            \set ON_ERROR_STOP off
            {statement}
            \set spoof_status :SQLSTATE
            \set ON_ERROR_STOP on
            ROLLBACK TO SAVEPOINT spoof;
            {observed_journal(db, shared_key, status=":'spoof_status'")}
            SAVEPOINT write_probe;
            \set ON_ERROR_STOP off
            {update}
            \set write_status :SQLSTATE
            \set ON_ERROR_STOP on
            ROLLBACK TO SAVEPOINT write_probe;
            SELECT :'write_status';
            ROLLBACK;
        """, role=login, states=(DENIED, DENIED))
        read, write_status = raw.splitlines()
        value = json.loads(read)
        require(value['attemptSqlstate'] == DENIED and write_status == DENIED.encode('ascii'),
                'spoof or same-session update SQLSTATE differs')
        assert_observed(value, login, db.a, rows, 0)

    raw = db.sql('007-guc', rf"""
        BEGIN; SET LOCAL search_path=pg_catalog,cp6_tenancy_v1,pg_temp;
        SET LOCAL app.tenant_id={Q(db.b)};
        {observed_journal(db, shared_key)}
        SAVEPOINT write_probe;
        \set ON_ERROR_STOP off
        {update}
        \set write_status :SQLSTATE
        \set ON_ERROR_STOP on
        ROLLBACK TO SAVEPOINT write_probe;
        SELECT :'write_status';
        ROLLBACK;
    """, role=login, states=(DENIED,))
    read, write_status = raw.splitlines()
    value = json.loads(read)
    assert_observed(value, login, db.a, rows, 0)
    require(value['guc'] == db.b and write_status == DENIED.encode('ascii'), 'spoof GUC or write not exercised')
    op = Q(str(uuid4()))
    actor = Q(str(uuid4()))
    db.deny('007-guc-b-store', f"""
        SET LOCAL app.tenant_id={Q(db.b)};
        SELECT cp6_rma_original_journal_v1.store({Q(db.b)}::uuid,{op}::uuid,{actor}::uuid,'CLOSED',
          NULL::bytea,NULL::text,convert_to(json_build_object('OperationId',{op}::uuid,'State','CLOSED','Result',NULL)::text,
          'UTF8'),ARRAY[]::uuid[],'{{}}'::jsonb);
    """)
    require(b'row-level security policy' in (db.output / '007-guc-b-store.stderr').read_bytes(),
            'trusted store did not reach its RLS WITH CHECK')


def catalog(db, label, sequence, unknown):
    a, b, owner = map(Q, (db.args.login_a, db.args.login_b, db.args.owner))
    schemas = ','.join(map(Q, ('public', 'cp6_tenancy_v1', 'cp6_main_owner_v1',
                              'cp6_rma_original_journal_v1', db.a_schema, db.b_schema)))
    return db.read(label, f"""
        WITH selected AS (SELECT oid,rolname,rolcanlogin,rolsuper,rolbypassrls,rolcreatedb,rolcreaterole,rolreplication,rolinherit
            FROM pg_roles WHERE rolname IN ({a},{b},'cp6_tenant_lookup_v1')),
        relations AS (SELECT oid,relname,relkind,relowner,relnamespace,relacl,relrowsecurity,relforcerowsecurity
            FROM pg_class WHERE oid IN ('{db.a_schema}.heads'::regclass,'{db.a_schema}.versions'::regclass,
                '{db.b_schema}.heads'::regclass,'{db.b_schema}.versions'::regclass,'{JOURNAL}'::regclass,
                '{sequence}'::regclass,'{unknown}'::regclass))
        SELECT jsonb_build_object(
          'roles',(SELECT jsonb_object_agg(rolname,jsonb_build_object('oid',oid,'login',rolcanlogin,'super',rolsuper,
            'bypass',rolbypassrls,'createdb',rolcreatedb,'createrole',rolcreaterole,'replication',rolreplication,
            'inherit',rolinherit)) FROM selected),
          'memberships',(SELECT coalesce(jsonb_agg(to_jsonb(m) ORDER BY m.roleid,m.member,m.grantor),'[]'::jsonb)
            FROM pg_auth_members m WHERE m.member IN (SELECT oid FROM selected) OR m.roleid IN (SELECT oid FROM selected)),
          'database',(SELECT jsonb_build_object('owner',pg_get_userbyid(datdba),'acl',datacl)
            FROM pg_database WHERE datname=current_database()),
          'schemas',(SELECT jsonb_agg(jsonb_build_array(nspname,pg_get_userbyid(nspowner),nspacl) ORDER BY nspname)
            FROM pg_namespace WHERE nspname IN ({schemas})),
          'defaults',(SELECT coalesce(jsonb_agg(jsonb_build_array(pg_get_userbyid(d.defaclrole),n.nspname,
              d.defaclobjtype,d.defaclacl) ORDER BY d.defaclrole,d.defaclnamespace,d.defaclobjtype),'[]'::jsonb)
            FROM pg_default_acl d LEFT JOIN pg_namespace n ON d.defaclnamespace=n.oid
            WHERE d.defaclnamespace=0 OR n.nspname IN ({schemas})),
          'objects',(SELECT jsonb_agg(to_jsonb(r) ORDER BY oid) FROM relations r),
          'columnAcl',(SELECT jsonb_agg(jsonb_build_array(a.attrelid,a.attnum,a.attname,a.attacl) ORDER BY a.attrelid,a.attnum)
            FROM pg_attribute a WHERE a.attrelid IN (SELECT oid FROM relations) AND a.attnum>0 AND NOT a.attisdropped),
          'journalRls',(SELECT jsonb_build_array(relrowsecurity,relforcerowsecurity) FROM relations WHERE oid='{JOURNAL}'::regclass),
          'policies',(SELECT jsonb_agg(jsonb_build_array(polname,polcmd,polpermissive,polroles,
              pg_get_expr(polqual,polrelid),pg_get_expr(polwithcheck,polrelid)) ORDER BY polname)
            FROM pg_policy WHERE polrelid='{JOURNAL}'::regclass),
          'helper',(SELECT jsonb_build_object('owner',pg_get_userbyid(proowner),'definer',prosecdef,
              'config',proconfig,'acl',coalesce(proacl,acldefault('f',proowner)),'definition',pg_get_functiondef(oid),
              'publicExecute',EXISTS(SELECT FROM aclexplode(coalesce(proacl,acldefault('f',proowner))) WHERE grantee=0))
            FROM pg_proc WHERE oid='{HELPER}'::regprocedure),
          'effective',jsonb_build_object('publicCreate',has_schema_privilege({a},'public','CREATE'),
            'temp',has_database_privilege({a},current_database(),'TEMP'),
            'ownUsage',has_schema_privilege({a},'{db.a_schema}','USAGE'),
            'helperA',has_function_privilege({a},'{HELPER}','EXECUTE'),
            'helperB',has_function_privilege({b},'{HELPER}','EXECUTE'),
            'unknownRead',has_table_privilege({a},'{unknown}','SELECT'),
            'unknownOwner',pg_get_userbyid((SELECT relowner FROM relations WHERE oid='{unknown}'::regclass))={owner},
            'seqA',has_sequence_privilege({a},'{sequence}','USAGE'),
            'seqB',has_sequence_privilege({b},'{sequence}','USAGE')));
    """)


def check_catalog(value, db):
    require(value['memberships'] == [] and value['journalRls'] == [True, True], 'role membership or forced RLS differs')
    for login in (db.args.login_a, db.args.login_b, 'cp6_tenant_lookup_v1'):
        role = value['roles'][login]
        require(role['login'] == (login != 'cp6_tenant_lookup_v1')
                and not any(role[key] for key in ('super', 'bypass', 'createdb', 'createrole', 'replication')),
                'ordinary or helper role is privileged')
    helper = value['helper']
    require(helper['owner'] == 'cp6_tenant_lookup_v1' and helper['definer'] and not helper['publicExecute']
            and helper['config'] == ['search_path=pg_catalog, cp6_tenancy_v1, pg_temp']
            and 'session_user' in helper['definition'], 'actual helper metadata differs')
    require(value['effective'] == dict(publicCreate=False,temp=False,ownUsage=True,helperA=True,helperB=True,
                                      unknownRead=False,unknownOwner=True,seqA=False,seqB=True), 'effective ACL differs')


def check_namespace(db, unknown, shared_key, rows):
    for label, statement in (
        ('public-table', 'CREATE TABLE public.w417_shadow(id integer);'),
        ('temporary-table', 'CREATE TEMP TABLE w417_shadow(id integer);'),
        ('public-shadow-function', f'CREATE FUNCTION public.current_tenant_id() RETURNS uuid LANGUAGE sql AS $$SELECT {Q(db.b)}::uuid$$;'),
        ('replace-helper', f'CREATE OR REPLACE FUNCTION {HELPER} RETURNS uuid LANGUAGE sql AS $$SELECT {Q(db.b)}::uuid$$;'),
        ('unknown-table', f'SELECT id FROM {unknown};'),
    ):
        db.deny('009-' + label, statement)
    result = db.read('009-hostile-path-helper', f"""
        BEGIN; SET LOCAL search_path=pg_temp,public,cp6_tenancy_v1,pg_catalog;
        SET LOCAL app.tenant_id={Q(db.b)};
        {observed_journal(db, shared_key)}
        ROLLBACK;
    """, role=db.args.login_a)
    assert_observed(result, db.args.login_a, db.a, rows, 0)
    require(result['guc'] == db.b and result['path'].startswith('pg_temp, public,'), 'hostile input was not active')


def run(args):
    db, expected = guarded_inputs(args)
    before = sample_state(db, 'protected-before', db.a, db.b)
    protected_pin = archive.file_digest(db.output / 'protected-before.stdout')
    require(before == expected and len(before['bHeads']) == len(before['bVersions']) == 1, 'W413 original tenant state differs')
    raw_before = originals(db, 'all-originals-before')
    require(originals(db, 'a-originals-before', only_a=True) == args.prior_a_original.read_bytes(),
            'exact A original journal bytes differ')
    shared, = [r for r in before['otherOriginals'] if r['tenant_id'] == db.b and r['owner'] == archive.JOURNAL_OWNER]
    shared_key = shared['operation_id']
    expected_rows = expected_journal(db)
    require(len(expected_rows[db.a]) == 2 and len(expected_rows[db.b]) == 1
            and [row[2] for row in expected_rows[db.a] if row[1] == shared_key] == ['COMMITTED']
            and expected_rows[db.b][0][1:3] == [shared_key, 'CLOSED'], 'missing original A/B shared key')
    for role, tenant, count in ((args.login_a, db.a, 0), (args.login_b, db.b, 1)):
        assert_observed(ordinary_read(db, 'ordinary-' + ('a' if tenant == db.a else 'b'), role, shared_key),
                        role, tenant, expected_rows[tenant], count)

    label = uuid4().hex[:12]
    sequence = f'{db.b_schema}.w417_b_sequence_{label}'
    unknown = f'{db.a_schema}.w417_ungranted_{label}'
    db.sql('only-synthetic-probes', f"""
        BEGIN;
        CREATE SEQUENCE {sequence} START WITH 101 INCREMENT BY 7;
        GRANT USAGE ON SEQUENCE {sequence} TO {ident(args.login_b)};
        CREATE TABLE {unknown}(id integer PRIMARY KEY);
        INSERT INTO {unknown} VALUES (1);
        COMMIT;
    """)
    value = db.read('b-sequence-positive', f'SELECT jsonb_build_array(session_user,nextval({Q(sequence)}::regclass));',
                    role=args.login_b)
    require(value == [args.login_b, 101], 'B did not execute original positive nextval on the synthetic sequence')
    sample_seq = f'SELECT jsonb_build_array(last_value,is_called) FROM {sequence};'
    seq = db.read('b-sequence-before', sample_seq)
    require(seq == [101, True], 'B synthetic sequence baseline differs')
    acl = catalog(db, 'catalog-before', sequence, unknown)
    check_catalog(acl, db)
    try:
        check_cross_schema(db, before, sequence)
        check_spoof(db, shared_key, expected_rows[db.a])
        check_namespace(db, unknown, shared_key, expected_rows[db.a])
    finally:
        after = sample_state(db, 'protected-after', db.a, db.b)
        raw_after = originals(db, 'all-originals-after')
        seq_after = db.read('b-sequence-after', sample_seq)
        acl_after = catalog(db, 'catalog-after', sequence, unknown)
        require(before == after and protected_pin == archive.file_digest(db.output / 'protected-after.stdout')
                and raw_before == raw_after and seq == seq_after and acl == acl_after,
                'native protected rows, sequence or permission catalog changed')

    result = dict(cards=['PGT-006','PGT-007','PGT-009'], target=db.target.mapping(), sharedKey=shared_key,
                  a=db.a, b=db.b, bSequence=sequence, bSequenceUnchanged=seq, unknownTable=unknown,
                  protectedStateUnchanged=protected_pin,
                  originalsUnchanged=archive.file_digest(db.output / 'all-originals-before.stdout'), commands=db.results)
    (db.output / 'actual.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: result[k] for k in ('cards','target','sharedKey','bSequenceUnchanged','originalsUnchanged')}))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for field in ('pg-bin','copy-receipt','prior-state','prior-a-original','journal-receipt','output'):
        parser.add_argument('--' + field, type=Path, required=True)
    for field in ('owner','login-a','login-b'):
        parser.add_argument('--' + field, required=True)
    for field in ('tenant-a','tenant-b'):
        parser.add_argument('--' + field, type=archive.guid, required=True)
    try:
        run(parser.parse_args())
    except (OSError, KeyError, ValueError, subprocess.SubprocessError) as error:
        print('native ACL verification failed:', error, file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
