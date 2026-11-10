#!/usr/bin/env python3
"""Import one original C03 missing-partner result into a closed PostgreSQL candidate."""
import argparse
import json
from pathlib import Path
import stat
import sys

sys.dont_write_bytecode = True
import archive
import candidate_core as candidate
import erp_integration as erp
import file_content_assets as assets
import file_content_history as paths
import stage

PROFILE = "cp6-erp-c03-partner-terminal-candidate-v1"
RECEIPTS = "cp6_erp_c03_candidate_import_v1"
PLAN = candidate.PLAN
SEQUENCE = "cp6_storage_erp.rowversion_v1"
TOKEN_FUNCTION = "cp6_storage_erp.set_rowversion_v1()"
TOKEN_BODY = """BEGIN NEW."RowVersion" := pg_catalog.int8send(nextval('"cp6_storage_erp".rowversion_v1'::regclass)); RETURN NEW; END"""
REQUEST = "com.gtx537.crm.erp.business-partner-requested.v1"
RESULT = "com.gtx537.erp.business-partner-failed.v1"
FAILURE = "C03_BUSINESS_PARTNER_NOT_FOUND"
NAMES = ("Aggregate", "Request", "CommandInbox", "Cp6_OutboxMessage")
COUNTS = {table.name: int(table.name in NAMES) for table in erp.TABLES}
TABLES = {table.name: table for table in erp.TABLES}
TOKENS = tuple(table.name for table in erp.TABLES if "RowVersion" in table.columns)
Q = archive.literal


def relation(name):
    return f'erp_integration."{name}"'


def temporary(name):
    return f'pg_temp."erp_c03_{name}"'


def columns(name):
    return ','.join(f'"{column}"' for column in TABLES[name].columns)


def closed_sql():
    return f"""
    DO $erp_candidate_closed$
    DECLARE
        db pg_database%ROWTYPE;
        tenant uuid := (current_setting('{PLAN}')::jsonb->'key'->>'tenantId')::uuid;
        owner oid := to_regrole(quote_ident(current_user));
    BEGIN
        SELECT * INTO STRICT db FROM pg_database WHERE datname=current_database();
        IF EXISTS (SELECT FROM aclexplode(coalesce(db.datacl,acldefault('d',owner))) a WHERE a.grantee<>owner)
           OR EXISTS (SELECT FROM pg_stat_activity WHERE datid=db.oid AND pid<>pg_backend_pid()
                       AND backend_type='client backend') THEN
            RAISE EXCEPTION 'rma-stage-erp-candidate-not-isolated';
        END IF;
        IF current_setting('session_replication_role')<>'origin'
           OR EXISTS (SELECT FROM pg_event_trigger WHERE evtenabled<>'D')
           OR EXISTS (SELECT FROM pg_default_acl WHERE defaclrole=owner AND defaclobjtype IN ('r','n'))
           OR (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE c.oid IN (to_regclass('cp6_privacy_disposition_v1.restore_admission'),
                                to_regclass('cp6_scope_authorization_v1.environment')) AND c.relkind='r'
                  AND c.relowner=owner AND n.nspowner=owner AND NOT c.relrowsecurity)<>2
           OR NOT EXISTS (SELECT FROM pg_proc WHERE oid=to_regprocedure('cp6_authentication_v1.acquire()')
                           AND proowner=owner) THEN
            RAISE EXCEPTION 'rma-stage-erp-candidate-layout-rejected';
        END IF;
        PERFORM cp6_authentication_v1.acquire();
        IF EXISTS (SELECT FROM cp6_privacy_disposition_v1.restore_admission)
           OR EXISTS (SELECT FROM cp6_scope_authorization_v1.environment) THEN
            RAISE EXCEPTION 'rma-stage-erp-candidate-admission-exists';
        END IF;
        IF NOT EXISTS (SELECT FROM public."Sys_Tenants" WHERE "Id"=tenant) THEN
            RAISE EXCEPTION 'rma-stage-erp-candidate-tenant-rejected';
        END IF;
    END
    $erp_candidate_closed$;
    LOCK TABLE {','.join(map(relation, TABLES))} IN ACCESS EXCLUSIVE MODE;
    """


def layout_sql():
    expected = []
    keys = []
    for name, table in TABLES.items():
        types = {**erp.COLUMN_TYPES, **erp.TABLE_TYPES.get(name, {})}
        shape = [[col, types.get(col, 'bpchar')] for col in table.columns]
        expected.append(f"({Q(name)},{Q(json.dumps(shape))}::jsonb)")
        for index, key in enumerate(table.unique_keys):
            keys.append(f"({Q(name)},{'true' if index == 0 else 'false'},ARRAY[{','.join(map(Q, key))}])")
    empty = " OR ".join(f'EXISTS (SELECT FROM {relation(name)} WHERE "TenantId"=tenant)' for name in TABLES)
    collation_values = ','.join(f"({Q(name)},{Q(column)},{slot},{Q(schema)},{Q(collation)},{str(det).lower()})"
                                for name, column, slot, schema, collation, det in erp.KEY_COLLATIONS)
    return f"""
    DO $erp_candidate_layout$
    DECLARE
        owner oid := to_regrole(quote_ident(current_user));
        tenant uuid := (current_setting('{PLAN}')::jsonb->'key'->>'tenantId')::uuid;
    BEGIN
        IF NOT EXISTS (SELECT FROM public."{erp.HISTORY}" WHERE "MigrationId"='{erp.MIGRATION}')
           OR (SELECT count(*) FROM pg_namespace n WHERE n.nspname IN ('erp_integration','cp6_storage_erp')
               AND n.nspowner=owner AND NOT EXISTS (SELECT FROM aclexplode(n.nspacl) a WHERE a.grantee<>owner))<>2
           OR (SELECT count(*) FROM pg_class c WHERE c.relnamespace='erp_integration'::regnamespace
                                                  AND c.relkind<>'i')<>8
           OR (SELECT count(*) FROM (VALUES {','.join(expected)}) w(name,columns)
                JOIN pg_class c ON c.relname=w.name AND c.relnamespace='erp_integration'::regnamespace
                WHERE c.relkind='r' AND c.relpersistence='p' AND c.relowner=owner AND NOT c.relrowsecurity
                  AND NOT c.relforcerowsecurity AND NOT EXISTS (SELECT FROM pg_policy p WHERE p.polrelid=c.oid)
                  AND NOT EXISTS (SELECT FROM pg_constraint x WHERE x.conrelid=c.oid AND x.contype='f')
                  AND NOT EXISTS (SELECT FROM aclexplode(c.relacl) a WHERE a.grantee<>owner)
                  AND NOT EXISTS (SELECT FROM pg_attribute a,LATERAL aclexplode(a.attacl) acl
                                  WHERE a.attrelid=c.oid AND acl.grantee<>owner)
                  AND EXISTS (SELECT FROM pg_attribute a WHERE a.attrelid=c.oid AND a.attname='TenantId' AND a.attnotnull)
                  AND w.columns=(SELECT jsonb_agg(jsonb_build_array(a.attname,t.typname) ORDER BY a.attnum)
                    FROM pg_attribute a JOIN pg_type t ON t.oid=a.atttypid WHERE a.attrelid=c.oid
                        AND a.attnum>0 AND NOT a.attisdropped))<>8
           OR (SELECT count(*) FROM pg_index i JOIN pg_class c ON c.oid=i.indrelid
                 WHERE c.relnamespace='erp_integration'::regnamespace AND i.indisunique)<>{len(keys)}
           OR (SELECT count(*) FROM (VALUES {','.join(keys)}) w(name,primary_key,columns)
                 JOIN pg_class c ON c.relname=w.name AND c.relnamespace='erp_integration'::regnamespace
                 JOIN pg_index i ON i.indrelid=c.oid AND i.indisunique AND i.indisprimary=w.primary_key
                   AND i.indisvalid AND i.indisready AND i.indislive AND i.indimmediate
                   AND i.indpred IS NULL AND i.indexprs IS NULL AND i.indnatts=i.indnkeyatts
                 WHERE w.columns=ARRAY(SELECT a.attname::text FROM unnest(i.indkey) WITH ORDINALITY k(attnum,ord)
                      JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum=k.attnum ORDER BY k.ord))<>{len(keys)}
           OR (SELECT count(*) FROM (VALUES {collation_values}) w(tab,col,slot,schema_name,collation_name,deterministic)
                 JOIN pg_class t ON t.relnamespace='erp_integration'::regnamespace AND t.relname=w.tab
                 JOIN pg_attribute a ON a.attrelid=t.oid AND a.attname=w.col AND NOT a.attisdropped
                 JOIN pg_collation c ON c.oid=a.attcollation AND c.collname=w.collation_name AND c.collisdeterministic=w.deterministic
                 JOIN pg_namespace n ON n.oid=c.collnamespace AND n.nspname=w.schema_name
                 JOIN pg_index i ON i.indrelid=t.oid AND i.indisunique AND i.indnkeyatts=w.slot+1
                    AND i.indkey[w.slot]=a.attnum AND i.indcollation[w.slot]=a.attcollation
                 WHERE w.deterministic OR (c.collprovider='i' AND c.collencoding=-1
                    AND COALESCE(to_jsonb(c)->>'colllocale',to_jsonb(c)->>'colliculocale') COLLATE "C"='und-u-ks-level2'
                    AND COALESCE(to_jsonb(c)->>'collicurules','')=''))<>{len(erp.KEY_COLLATIONS)}
           OR NOT EXISTS (SELECT FROM pg_class c JOIN pg_sequence s ON s.seqrelid=c.oid
                WHERE c.oid='{SEQUENCE}'::regclass AND c.relkind='S' AND c.relpersistence='p' AND c.relowner=owner
                  AND NOT EXISTS (SELECT FROM aclexplode(c.relacl) a WHERE a.grantee<>owner)
                  AND s.seqtypid='bigint'::regtype AND s.seqstart=1 AND s.seqincrement=1 AND s.seqmin=1
                  AND s.seqmax=9223372036854775807 AND s.seqcache=1 AND NOT s.seqcycle)
           OR NOT EXISTS (SELECT FROM pg_proc p JOIN pg_language l ON l.oid=p.prolang
                WHERE p.oid='{TOKEN_FUNCTION}'::regprocedure AND p.proowner=owner AND p.prorettype='trigger'::regtype
                  AND l.lanname='plpgsql' AND NOT p.prosecdef AND p.proconfig IS NULL
                  AND btrim(regexp_replace(p.prosrc,'[[:space:]]+',' ','g'))={Q(TOKEN_BODY)})
           OR (SELECT count(*) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
                WHERE c.relnamespace='erp_integration'::regnamespace AND NOT t.tgisinternal)<>6
           OR (SELECT count(*) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
                WHERE c.relnamespace='erp_integration'::regnamespace AND c.relname IN ({','.join(map(Q, TOKENS))})
                  AND NOT t.tgisinternal AND t.tgname='cp6_rowversion_v1' AND t.tgenabled='O'
                  AND t.tgtype=23 AND t.tgnargs=0 AND t.tgqual IS NULL AND t.tgfoid='{TOKEN_FUNCTION}'::regprocedure)<>6 THEN
            RAISE EXCEPTION 'rma-stage-erp-candidate-layout-rejected';
        END IF;
        IF {empty} THEN RAISE EXCEPTION 'rma-stage-erp-candidate-not-empty'; END IF;
    END
    $erp_candidate_layout$;
    """


def staged_sql():
    return archive.JOURNAL_COPY_SETTINGS + '\n' + '\n'.join(f"""
    CREATE TEMP TABLE "erp_c03_{name}" (LIKE {relation(name)} INCLUDING CONSTRAINTS) ON COMMIT DROP;
    \\copy {temporary(name)} ({columns(name)}) FROM 'bundle/{table.member}' WITH (FORMAT csv, HEADER MATCH)
    """ for name, table in TABLES.items())


def graph_sql():
    counts = ' OR '.join(f'(SELECT count(*) FROM {temporary(name)})<>{count}' for name, count in COUNTS.items())
    a, r, i, o = map(temporary, NAMES)
    return f"""
    DO $erp_candidate_graph$
    DECLARE
        tenant uuid := (current_setting('{PLAN}')::jsonb->'key'->>'tenantId')::uuid;
        valid boolean;
    BEGIN
        IF {counts} THEN RAISE EXCEPTION 'rma-stage-erp-candidate-shape-rejected'; END IF;
        SELECT a."TenantId"=tenant AND r."TenantId"=tenant AND i."TenantId"=tenant AND o."TenantId"=tenant
          AND a."Kind"=1 AND r."Kind"=1 AND a."AggregateId"=r."AggregateId" AND a."AggregateId"=i."AggregateId"
          AND r."AccountId"=a."AggregateId" AND o."AggregateId"::text COLLATE "C"=a."AggregateId"::text
          AND r."AccountId"<>'00000000-0000-0000-0000-000000000000'::uuid
          AND r."RequestId"<>'00000000-0000-0000-0000-000000000000'::uuid
          AND a."LastRequestVersion"=r."RequestVersion" AND a."LastAggregateVersion"=i."AggregateVersion"
          AND a."LastResultVersion"=r."ResultVersion" AND o."AggregateVersion"=r."ResultVersion"
          AND r."RequestVersion">0 AND i."AggregateVersion">0 AND r."ResultVersion">0
          AND r."Terminal" AND NOT r."Succeeded" AND r."ResultType"::text COLLATE "C"='{RESULT}'
          AND i."Status"=1 AND i."AttemptCount"=1 AND i."ConflictCount"=0 AND i."ProcessedAtUtc" IS NOT NULL
          AND i."RetryAtUtc" IS NULL AND i."ErrorCode" IS NULL AND i."LastConflictSha256" IS NULL
          AND i."ReplayedAtUtc" IS NULL AND i."ReplayReasonCode" IS NULL
          AND o."Status"=0 AND o."AttemptCount"=0 AND o."LeaseOwner" IS NULL AND o."LeaseToken" IS NULL
          AND o."LeaseExpiresAtUtc" IS NULL AND o."PublishedAtUtc" IS NULL AND o."DeadLetteredAtUtc" IS NULL
          AND o."LastErrorCode" IS NULL AND o."SupportReference" IS NULL
          AND o."MessageId"::text COLLATE "C"<>i."MessageId"::text AND i."EventType"::text COLLATE "C"='{REQUEST}'
          AND o."TopicName"::text COLLATE "C"='cp6.erp.events.v1'
          AND o."PartitionKey"::text COLLATE "C"=tenant::text||':'||r."AccountId"::text
          AND o."CausationId"::text COLLATE "C"=i."MessageId"::text
          AND i."PayloadSha256"::text COLLATE "C"=encode(sha256(i."Payload"),'hex')
          AND o."PayloadSha256"::text COLLATE "C"=encode(sha256(o."Payload"),'hex')
          AND r."InputSha256"::text COLLATE "C"=encode(sha256(convert_to(format(
              '{{"tenantId":"%s","requestId":"%s","accountId":"%s","requestVersion":%s}}',
               tenant,r."RequestId",r."AccountId",r."RequestVersion"),'UTF8')),'hex')
          AND command.body @> jsonb_build_object('id',i."MessageId"::text,'tenantid',tenant,
               'type','{REQUEST}','source','urn:cp6:crm','aggregateid',r."AccountId",'aggregateversion',i."AggregateVersion",
               'data',jsonb_build_object('tenantId',tenant,'requestId',r."RequestId",'accountId',r."AccountId",
                                         'requestVersion',r."RequestVersion"))
          AND result.body @> jsonb_build_object('id',o."MessageId"::text,'tenantid',tenant,
               'type','{RESULT}','source','urn:cp6:erp','aggregateid',r."AccountId",'aggregateversion',r."ResultVersion",
               'correlationid',o."CorrelationId"::text,'causationid',i."MessageId"::text)
          AND (command.body->>'correlationid') COLLATE "C"=o."CorrelationId"::text
          AND r."ResultDataJson"::text::jsonb=result.body->'data'
          AND result.body->'data'=jsonb_build_object('tenantId',tenant,'requestId',r."RequestId",'accountId',r."AccountId",
               'requestVersion',r."RequestVersion",'resultVersion',r."ResultVersion",'errorCode','{FAILURE}','retryable',false)
        INTO valid
        FROM {a} a CROSS JOIN {r} r CROSS JOIN {i} i CROSS JOIN {o} o
        CROSS JOIN LATERAL (SELECT convert_from(i."Payload",'UTF8')::jsonb AS body) command
        CROSS JOIN LATERAL (SELECT convert_from(o."Payload",'UTF8')::jsonb AS body) result;
        IF valid IS DISTINCT FROM true THEN RAISE EXCEPTION 'rma-stage-erp-candidate-graph-rejected'; END IF;
    END
    $erp_candidate_graph$;
    """


def allocate_sql():
    token = "CASE WHEN octet_length(\"RowVersion\")=8 THEN ('x'||encode(\"RowVersion\",'hex'))::bit(64)::bigint END"
    tables = [*(relation(name) for name in TOKENS), *(temporary(name) for name in NAMES)]
    values = '\nUNION ALL\n'.join(f'SELECT {token} AS value FROM {table}' for table in tables)
    return f"""
    -- START is already 1. This takes the sequence's allocation lock without changing its current value.
    ALTER SEQUENCE {SEQUENCE} START WITH 1;
    DO $erp_candidate_tokens$
    DECLARE
        floor numeric;
        highest numeric;
        lowest bigint;
        next_value numeric;
        conflict boolean;
    BEGIN
        SELECT last_value::numeric + is_called::integer INTO STRICT floor FROM {SEQUENCE};
        SELECT min(value),max(value)::numeric,count(*)<>count(DISTINCT value)
          INTO lowest,highest,conflict FROM ({values}) tokens;
        next_value := greatest(floor,highest+1);
        IF lowest<=0 OR conflict OR next_value>9223372036854775807 THEN
            RAISE EXCEPTION 'rma-stage-erp-candidate-token-rejected';
        END IF;
        IF next_value>floor THEN EXECUTE format('ALTER SEQUENCE {SEQUENCE} RESTART WITH %s',next_value); END IF;
        PERFORM set_config('{PLAN}_erp_sequence_next',next_value::text,true);
    END
    $erp_candidate_tokens$;
    """


def adopt_sql():
    writes = '\n'.join(f"""
    ALTER TABLE ONLY {relation(name)} DISABLE TRIGGER cp6_rowversion_v1;
    INSERT INTO {relation(name)} ({columns(name)}) SELECT {columns(name)} FROM {temporary(name)};
    ALTER TABLE ONLY {relation(name)} ENABLE TRIGGER cp6_rowversion_v1;
    """ for name in NAMES)
    return writes + f"""
    CREATE SCHEMA {RECEIPTS};
    REVOKE ALL ON SCHEMA {RECEIPTS} FROM PUBLIC;
    CREATE TABLE {RECEIPTS}.receipt (
        singleton boolean PRIMARY KEY CHECK(singleton), input jsonb NOT NULL, result_json text NOT NULL);
    REVOKE ALL ON {RECEIPTS}.receipt FROM PUBLIC;
    INSERT INTO {RECEIPTS}.receipt
    SELECT true,p->'key',json_build_object('outcome','IMPORTED','recoveryReady',false,'profile','{PROFILE}',
        'binding',p->'key','format',p->>'format','counts',p->'counts',
        'rowVersionNext',current_setting('{PLAN}_erp_sequence_next')::bigint)::text
    FROM (SELECT current_setting('{PLAN}')::jsonb p) s;
    SELECT result_json FROM {RECEIPTS}.receipt;
    """


def execute(args):
    pg = args.pg_bin.resolve(strict=True)
    target = archive.PhysicalTarget.from_mapping(json.loads(args.target_file.read_text(encoding='utf-8-sig')))
    tenant, candidate_id = archive.guid(args.tenant), archive.guid(args.candidate_id)
    pin = stage.digest_pin(args.manifest_sha256)
    if not args.username or '\0' in args.username:
        raise stage.StageError('rma-stage-invalid-username')
    recover = args.action == 'recover'
    output = paths.checked_output(args.output.absolute(), args.target_file.resolve(strict=True))
    if not recover:
        bundle = assets.checked_root(args.bundle.absolute()).resolve(strict=True)
        for entry in bundle.iterdir():
            assets.entry(entry, stat.S_ISREG, 'rma-stage-bundle-member-rejected')
        root = assets.checked_root(args.file_content_root)
        paths.checked_output(output, bundle)
        paths.checked_output(output, root)
    archive.private_directory(output)
    plan = dict(key=dict(target=target.mapping(), candidateId=str(candidate_id), tenantId=str(tenant), manifestSha256=pin))
    receipt = candidate.receipt_sql(schema=RECEIPTS, profile=PROFILE)
    if recover:
        driver = candidate.target_sql(plan, True) + receipt
    else:
        _, metadata = stage.checked_bundle(bundle, output, tenant, pin, target, root, include_stock_outbox=True)
        if metadata['format'] != archive.FORMAT_WITH_STOCK_OUTBOX or metadata.get('erpIntegrationCounts') != COUNTS:
            raise stage.StageError('rma-stage-erp-candidate-shape-rejected')
        plan.update(format=metadata['format'], counts=COUNTS)
        driver = candidate.target_sql(plan, False) + f"""
        SELECT to_regnamespace('{RECEIPTS}') IS NOT NULL AS has_receipt \\gset
        \\if :has_receipt
        {receipt}
        \\else
        {closed_sql()}
        {layout_sql()}
        {staged_sql()}
        {graph_sql()}
        {allocate_sql()}
        {adopt_sql()}
        \\endif
        """
    (output / 'driver.sql').write_text(driver, encoding='utf-8')
    return stage.run_driver(pg, target, args.username, output, 'recover' if recover else 'stage')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest='action', required=True)
    for name in ('import', 'recover'):
        command = actions.add_parser(name)
        for field in ('pg-bin', 'target-file', 'output'):
            command.add_argument('--' + field, type=Path, required=True)
        for field in ('username', 'tenant', 'candidate-id', 'manifest-sha256'):
            command.add_argument('--' + field, required=True)
        if name == 'import':
            command.add_argument('--bundle', type=Path, required=True)
            command.add_argument('--file-content-root', type=Path, required=True)
    try:
        sys.stdout.buffer.write(execute(parser.parse_args()))
    except (archive.ArchiveError, OSError, ValueError, stage.StageError) as error:
        print('ERP terminal candidate rejected:', error, file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
