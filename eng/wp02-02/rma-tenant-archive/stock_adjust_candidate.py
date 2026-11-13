#!/usr/bin/env python3
"""Restore one original Stock ADJ and its unclaimed notification into a closed candidate."""
import argparse
import csv
import hashlib
import json
from pathlib import Path
import shutil
import stat
import subprocess
import sys

sys.dont_write_bytecode = True
import archive
import candidate_core as candidate
import file_content_assets as assets
import file_content_history as paths
import stage
from stock_adjust_originals import EFFECTS, FORMAT as EFFECT_FORMAT

STOCK = archive.stock_outbox_module()
PROFILE = "cp6-stock-adjust-candidate-v1"
RECEIPTS = "cp6_stock_adjust_candidate_v1"
PLAN = candidate.PLAN
Q = archive.literal
CORE_SEQUENCE = "cp6_storage_core.rowversion_v1"
MOVEMENT_SEQUENCE = "cp6_stock_return_v1.movement_sequences_sequence_seq"
TOKEN_FUNCTION = "cp6_storage_core.set_rowversion_v1()"
SOURCE_PROOF = "cp6_stock_outbox_v1.source_proof()"
TOKEN_BODY = """BEGIN
  NEW."RowVersion" := pg_catalog.int8send(nextval('"cp6_storage_core".rowversion_v1'::regclass));
  RETURN NEW;
END"""
# The normalized body from 20261026001000_MainStockCreationOutboxV2.
SOURCE_PROOF_SHA256 = "f7bd0295e5a5711cd778de7c6992e574a640bc52af65cf9dae805cfbc718da70"
JOURNAL = STOCK.relation(STOCK.JOURNAL)
QUEUE = (STOCK.ROUTE_TABLE, STOCK.AUTHORITY, STOCK.NATIVE, STOCK.SOURCE, STOCK.BINDING)
ROWVERSIONS = EFFECTS[:3]
WRITES = (*EFFECTS, STOCK.JOURNAL, *QUEUE)


def relation(part):
    return part.relation if part in EFFECTS else STOCK.relation(part)


def columns(part):
    return ",".join(map(STOCK.ident, part.columns if part in EFFECTS else (name for name, _ in part.fields)))


def temporary(part):
    name = part.member.removesuffix(".csv").replace("-", "_")
    return f'pg_temp."stock_adjust_{name}"'


def closed_sql():
    return """
    DO $stock_adjust_closed$
    DECLARE
        db pg_database%ROWTYPE;
    BEGIN
        SELECT * INTO STRICT db FROM pg_database WHERE datname=current_database();
        IF EXISTS (SELECT FROM aclexplode(coalesce(db.datacl,acldefault('d',db.datdba))) a
                   WHERE a.grantee<>db.datdba)
           OR EXISTS (SELECT FROM pg_stat_activity WHERE datid=db.oid AND pid<>pg_backend_pid()
                      AND backend_type='client backend') THEN
            RAISE EXCEPTION 'rma-stage-stock-candidate-not-isolated';
        END IF;
        IF EXISTS (SELECT FROM cp6_privacy_disposition_v1.restore_admission)
           OR EXISTS (SELECT FROM cp6_scope_authorization_v1.environment) THEN
            RAISE EXCEPTION 'rma-stage-stock-candidate-admission-exists';
        END IF;
    END
    $stock_adjust_closed$;
    """


def layout_sql():
    guarded = (
        (STOCK.NATIVE, "stock_source_proof", SOURCE_PROOF, 5, True),
        (STOCK.JOURNAL, "authority_gate", "cp6_authentication_v1.before_change()", 62, False),
        (STOCK.JOURNAL, "immutable", "cp6_main_owner_v1.immutable()", 58, False),
        (STOCK.SOURCE, "stock_source_immutable", f'{STOCK.SCHEMA}.cp6_outbox_v2_immutable()', 27, False),
        (EFFECTS[0], "stock_root_writer", "cp6_stock_move_v1.reject_legacy_writer()", 31, False),
        *((part, "warehouse_draft_use", "cp6_warehouse_draft_v1.use_guard()", 23, False) for part in EFFECTS[:2]),
        *((part, "cp6_rowversion_v1", TOKEN_FUNCTION, 23, False) for part in ROWVERSIONS),
        (EFFECTS[3], "immutable", "cp6_rma_formal_v1.immutable()", 27, False),
        (EFFECTS[3], "no_truncate", "cp6_rma_formal_v1.immutable()", 34, False),
    )
    trigger_rows = ",".join(f"({Q(relation(part))},{Q(name)},{Q(function)},{kind},{str(deferred).lower()})"
                            for part, name, function, kind, deferred in guarded)
    shape_rows = ",".join(f"({Q(relation(part))},ARRAY[{','.join(map(Q,part.columns))}],"
                          f"ARRAY[{','.join(map(Q,part.types))}])" for part in EFFECTS)
    effect_keys = (
        (EFFECTS[0], ("Id",), ("TenantId", "WarehouseCd", "LocationCd", "ProductCd", "LotNo")),
        (EFFECTS[1], ("Id",), ("TenantId", "TxnNo")),
        (EFFECTS[2], ("Id",), ("TenantId", "Prefix", "DateKey")),
        (EFFECTS[3], ("sequence",), ("tenant_id", "movement_id")),
    )
    keys = ",".join(f"({Q(relation(part))},{str(primary).lower()},ARRAY[{','.join(map(Q, key))}])"
                    for part, pk, natural in effect_keys for primary, key in ((True, pk), (False, natural)))
    tables = ",".join(Q(relation(part)) + "::regclass" for part in WRITES)
    migrations = (*STOCK.MIGRATIONS, "20261030023000_RmaPhysicalReturnV1", "20261101023900_StockDirectMoveV1")
    return f"""
    LOCK TABLE {','.join(map(relation, (*EFFECTS, *STOCK.TABLES)))} IN ACCESS EXCLUSIVE MODE;
    DO $stock_adjust_layout$
    DECLARE
        owner oid := to_regrole(quote_ident(current_user));
    BEGIN
        IF current_setting('session_replication_role')<>'origin'
           OR EXISTS (SELECT FROM pg_event_trigger WHERE evtenabled<>'D')
           OR EXISTS (SELECT FROM pg_default_acl WHERE defaclrole=owner AND defaclobjtype IN ('r','n'))
           OR (SELECT count(*) FROM pg_class c WHERE c.oid IN ({tables})
                AND c.relowner=owner AND c.relkind='r' AND NOT c.relispartition) <> {len(WRITES)}
           OR (SELECT count(*) FROM public."__EFMigrationsHistory"
                WHERE "MigrationId" IN ({','.join(map(Q,migrations))})) <> {len(migrations)}
           OR EXISTS (SELECT FROM (VALUES {shape_rows}) e(rel, names, types)
                CROSS JOIN LATERAL (SELECT array_agg(a.attname::text ORDER BY a.attnum) AS names,
                    array_agg(format_type(a.atttypid,a.atttypmod) ORDER BY a.attnum) AS types
                    FROM pg_attribute a WHERE a.attrelid=e.rel::regclass AND a.attnum>0 AND NOT a.attisdropped) actual
                WHERE actual.names IS DISTINCT FROM e.names OR actual.types IS DISTINCT FROM e.types)
           OR EXISTS (SELECT FROM (VALUES {keys}) e(rel,primary_key,columns) WHERE NOT EXISTS (
                SELECT FROM pg_index i WHERE i.indrelid=e.rel::regclass AND i.indisprimary=e.primary_key
                  AND i.indisunique AND i.indisvalid AND i.indisready AND i.indpred IS NULL AND i.indexprs IS NULL
                  AND e.columns=ARRAY(SELECT a.attname::text FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(num,ord)
                    JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=k.num
                    WHERE k.ord<=i.indnkeyatts ORDER BY k.ord)))
           OR EXISTS (SELECT FROM (VALUES {trigger_rows}) e(rel,name,func,kind,deferred)
                WHERE NOT EXISTS (SELECT FROM pg_trigger t WHERE t.tgrelid=e.rel::regclass AND t.tgname=e.name
                   AND t.tgfoid=e.func::regprocedure AND t.tgtype=e.kind AND t.tgenabled='O'
                   AND t.tgqual IS NULL AND t.tgnargs=CASE WHEN e.name='warehouse_draft_use' THEN 1 ELSE 0 END
                   AND (e.name<>'warehouse_draft_use' OR encode(t.tgargs,'hex')='57617265686f757365436400')
                   AND t.tgdeferrable=e.deferred AND t.tginitdeferred=e.deferred AND NOT t.tgisinternal))
           OR (SELECT count(*) FROM (VALUES
                ({Q(TOKEN_FUNCTION)},{Q(hashlib.sha256(TOKEN_BODY.encode('utf-8')).hexdigest())},NULL::text[]),
                ({Q(SOURCE_PROOF)},{Q(SOURCE_PROOF_SHA256)},ARRAY['search_path=pg_catalog'])
              ) e(func,body_sha,settings) JOIN pg_proc p ON p.oid=e.func::regprocedure
                WHERE p.proowner=owner AND NOT p.prosecdef AND p.prorettype='trigger'::regtype
                  AND encode(sha256(convert_to(btrim(replace(p.prosrc,chr(13),''),E' \\t\\n'),'UTF8')),'hex')=e.body_sha
                  AND p.proconfig IS NOT DISTINCT FROM e.settings)<>2
           OR (SELECT count(*) FROM pg_class c JOIN pg_sequence s ON s.seqrelid=c.oid
                WHERE c.oid IN ('{CORE_SEQUENCE}'::regclass,'{MOVEMENT_SEQUENCE}'::regclass)
                  AND c.relowner=owner AND s.seqtypid='bigint'::regtype AND s.seqstart=1 AND s.seqmin=1
                  AND s.seqmax=9223372036854775807 AND s.seqincrement=1 AND s.seqcache=1 AND NOT s.seqcycle)<>2
           OR to_regclass(pg_get_serial_sequence({Q(relation(EFFECTS[3]))},'sequence'))
                IS DISTINCT FROM '{MOVEMENT_SEQUENCE}'::regclass
           OR NOT EXISTS (SELECT FROM pg_attribute WHERE attrelid={Q(relation(EFFECTS[3]))}::regclass
                            AND attname='sequence' AND attidentity='a')
           OR EXISTS (SELECT FROM (VALUES
                ({Q(STOCK.relation(STOCK.SOURCE))},{Q(JOURNAL)},'stock_source_original',ARRAY[2,4,3],ARRAY[1,2,3]),
                ({Q(STOCK.relation(STOCK.SOURCE))},{Q(STOCK.relation(STOCK.NATIVE))},NULL,ARRAY[1],ARRAY[1]),
                ({Q(STOCK.relation(STOCK.BINDING))},{Q(STOCK.relation(STOCK.NATIVE))},NULL,ARRAY[1],ARRAY[1]),
                ({Q(STOCK.relation(STOCK.BINDING))},{Q(STOCK.relation(STOCK.ROUTE_TABLE))},NULL,ARRAY[2],ARRAY[1])
              ) e(src,dst,name,src_key,dst_key)
              WHERE NOT EXISTS (SELECT FROM pg_constraint c WHERE c.contype='f' AND c.convalidated
                AND c.conrelid=e.src::regclass AND c.confrelid=e.dst::regclass
                AND c.conkey=e.src_key::smallint[] AND c.confkey=e.dst_key::smallint[]
                AND (e.name IS NULL OR c.conname=e.name)))
           OR (SELECT count(*) FROM pg_index i JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=i.indkey[0]
                JOIN pg_collation c ON c.oid=a.attcollation
                WHERE i.indrelid IN ({Q(STOCK.relation(STOCK.NATIVE))}::regclass,{Q(STOCK.relation(STOCK.BINDING))}::regclass)
                  AND a.attname='MessageId' AND i.indnkeyatts=1 AND i.indisunique AND i.indisvalid AND i.indisready
                  AND i.indpred IS NULL AND i.indexprs IS NULL AND i.indcollation[0]=a.attcollation
                  AND c.collnamespace='pg_catalog'::regnamespace AND c.collname='default')<>2 THEN
            RAISE EXCEPTION 'rma-stage-stock-candidate-layout-rejected';
        END IF;
    END
    $stock_adjust_layout$;
    """


def stage_sql():
    statements = []
    for part in WRITES:
        is_effect = part in EFFECTS
        source_xmin = ', "source_xmin" xid' if not is_effect and part.xmin else ""
        directory = "effects" if is_effect else "bundle"
        member = part.member if is_effect else archive.STOCK_MEMBER_PREFIX + part.member
        statements.append(rf"""
        CREATE TEMP TABLE {temporary(part)} (LIKE {relation(part)} INCLUDING CONSTRAINTS{source_xmin}) ON COMMIT DROP;
        \copy {temporary(part)} FROM '{directory}/{member}' WITH (FORMAT csv, HEADER MATCH)
        """)
    return archive.JOURNAL_COPY_SETTINGS + "\n".join(statements)


def audit_timestamp(value):
    # CLR serializes 100 ns local time. The Core timestamp column stores local microseconds.
    return rf"regexp_replace({value},'([.][0-9]{{6}})[0-9]+','\1')::timestamp"


def graph_sql():
    s, t, w, m, j, route, authority, native, source, binding = map(temporary, WRITES)
    scopes = []
    for part in (*EFFECTS, *STOCK.TABLES):
        if part is EFFECTS[3]:
            condition = "tenant_id=tenant"
        elif part in ROWVERSIONS or part.scope == "tenant":
            condition = '"TenantId"=tenant'
        elif part is STOCK.JOURNAL:
            condition = f"tenant_id=tenant AND owner={Q(STOCK.JOURNAL_OWNER)}"
        elif part.scope == "root":
            condition = '"RootId"=root'
        else:
            condition = f'''"NativeId" IN (SELECT "Id" FROM {STOCK.relation(STOCK.NATIVE)} WHERE "TenantId"=tenant)'''
        scopes.append(f"EXISTS (SELECT FROM {relation(part)} WHERE {condition})")
    return f"""
    DO $stock_adjust_graph$
    DECLARE
        p jsonb := current_setting('{PLAN}')::jsonb;
        tenant uuid := (p->'key'->>'tenantId')::uuid;
        operation uuid := (p->>'operationId')::uuid;
        root text := p->>'root';
        valid boolean;
    BEGIN
        IF {' OR '.join(f'(SELECT count(*) FROM {temporary(part)})<>1' for part in WRITES)} THEN
            RAISE EXCEPTION 'rma-stage-stock-candidate-shape-rejected';
        END IF;
        IF {' OR '.join(scopes)} THEN
            RAISE EXCEPTION 'rma-stage-stock-candidate-not-empty';
        END IF;
        SELECT s."TenantId"=tenant AND t."TenantId"=tenant AND w."TenantId"=tenant AND m.tenant_id=tenant
          AND j.tenant_id=tenant AND j.owner={Q(STOCK.JOURNAL_OWNER)} AND j.operation_id=operation
          AND j.contract={Q(STOCK.JOURNAL_CONTRACTS[0])} AND j.state='COMMITTED'
          AND j.input_sha256=encode(sha256(j.input_bytes),'hex') AND j.object_ids=ARRAY[s."Id"]
          AND j.audit_json->>'action'='adjust' AND j.audit_json->>'actor'=j.actor_id::text
          AND jsonb_array_length(j.audit_json->'before')=1 AND jsonb_array_length(j.audit_json->'after')=1
          AND jsonb_array_length(j.audit_json->'transactions')=1
          AND s."Id"<>'00000000-0000-0000-0000-000000000000'::uuid
          AND t."Id"<>'00000000-0000-0000-0000-000000000000'::uuid
          AND before->>'Id'=s."Id"::text AND before->>'TenantId'=tenant::text
          AND (to_jsonb(s)-ARRAY['RowVersion','CreateDate','ModifyDate','ReceiveDate','ExpiryDate'])=
              (after-ARRAY['RowVersion','CreateDate','ModifyDate','ReceiveDate','ExpiryDate'])
          AND (to_jsonb(t)-ARRAY['RowVersion','TxnDateTime','CreateDate','ModifyDate'])=
              (audit_txn-ARRAY['RowVersion','TxnDateTime','CreateDate','ModifyDate'])
          AND s."RowVersion"=decode(after->>'RowVersion','base64')
          AND t."RowVersion"=decode(audit_txn->>'RowVersion','base64')
          AND octet_length(decode(before->>'RowVersion','base64'))=8
          AND s."RowVersion"<>decode(before->>'RowVersion','base64')
          AND s."CreateDate"={audit_timestamp("after->>'CreateDate'")}
          AND s."ModifyDate" IS NOT DISTINCT FROM {audit_timestamp("after->>'ModifyDate'")}
          AND s."ReceiveDate" IS NOT DISTINCT FROM {audit_timestamp("after->>'ReceiveDate'")}
          AND s."ExpiryDate" IS NOT DISTINCT FROM {audit_timestamp("after->>'ExpiryDate'")}
          AND t."TxnDateTime"={audit_timestamp("audit_txn->>'TxnDateTime'")}
          AND t."CreateDate"={audit_timestamp("audit_txn->>'CreateDate'")}
          AND t."ModifyDate" IS NOT DISTINCT FROM {audit_timestamp("audit_txn->>'ModifyDate'")}
          AND request.body->>'kind'='APPLY' AND request.body->'request' @> jsonb_build_object(
                'OperationId',operation,'TxnType','ADJ','Qty',t."Qty",'WarehouseCd',s."WarehouseCd"::text,
                'LocationCd',s."LocationCd"::text,'ProductCd',s."ProductCd"::text,'LotNo',s."LotNo"::text)
          AND result.body @> jsonb_build_object('Result',jsonb_build_object('OperationId',operation,'Kind','APPLY',
                'TxnNo',t."TxnNo"::text,'OutTxnNo',NULL,'InTxnNo',NULL))
          AND result.body->'StockIds'=jsonb_build_array(s."Id")
          AND result.body->'TransactionIds'=jsonb_build_array(t."Id")
          AND s."PhysicalQty"=(before->>'PhysicalQty')::numeric+t."Qty"
          AND s."AllocatedQty"=(before->>'AllocatedQty')::numeric AND s."AvailableQty"=s."PhysicalQty"-s."AllocatedQty"
          AND (t."WarehouseCd",t."LocationCd",t."ProductCd",t."LotNo")=(s."WarehouseCd",s."LocationCd",s."ProductCd",s."LotNo")
          AND t."TxnType"::text='ADJ' AND t."RelatedNo" IS NULL AND t."RelatedType" IS NULL
          AND NOT s."IsDeleted" AND NOT t."IsDeleted" AND NOT w."IsDeleted"
          AND w."Prefix"::text='TXN' AND w."DateKey"::text='*' AND w."NextNo">0
          AND t."TxnNo"::text COLLATE "C" ~ '^TXN[0-9]{{4}}(0[1-9]|1[0-2])[0-9]+$'
          AND t."TxnNo"::text COLLATE "C"='TXN'||substring(t."TxnNo"::text FROM 4 FOR 6)
                ||lpad(w."NextNo"::text,greatest(4,length(w."NextNo"::text)),'0')
          AND m.sequence>0 AND m.pool_id=s."Id" AND m.movement_id=t."Id"
          AND NOT EXISTS (SELECT FROM cp6_operations_owner_v1.department_mappings d WHERE d.tenant_id=tenant
                 AND ((d.object_kind='stock' AND d.object_id=s."Id") OR (d.object_kind='stock-transaction' AND d.object_id=t."Id")))
          AND NOT EXISTS (SELECT FROM public."Sys_SecurityLogs" x WHERE x."TenantId"=tenant
                   AND x."ContextJson"->>'kind'='stock-transaction-inheritance.v1'
                   AND x."ContextJson"->>'transactionId'=t."Id"::text)
          AND EXISTS (SELECT FROM public."Sys_Tenants" x WHERE x."Id"=tenant)
          AND EXISTS (SELECT FROM public."Sys_Users" x WHERE x."TenantId"=tenant AND x."Id"=j.actor_id)
          AND EXISTS (SELECT FROM public."T_Warehouse" x
                      WHERE x."TenantId"=tenant AND x."WarehouseCd"=s."WarehouseCd" AND NOT x."IsDeleted")
          AND EXISTS (SELECT FROM public."T_Location" x
                      WHERE x."TenantId"=tenant AND (x."WarehouseCd",x."LocationCd")=(s."WarehouseCd",s."LocationCd")
                        AND NOT x."IsDeleted" AND NOT x."IsInitializationQuarantined")
          AND route."TenantId"=tenant AND route."Id"=root AND route."LockKey"=(p->>'lockKey')::bigint
          AND route."LastSwitchId" IS NULL AND authority."TenantId"=tenant AND authority."RootId"=root
          AND native."TenantId"=tenant AND native."LastErrorCode" IS NULL AND native."SupportReference" IS NULL
          AND source."TenantId"=tenant AND source."OperationId"=operation AND source."NativeId"=native."Id"
          AND source."SourceOwner"=j.owner AND binding."RootId"=root AND binding."NativeId"=native."Id"
          AND binding."ClaimStartedAtUtc" IS NULL
        INTO valid
        FROM {s} s CROSS JOIN {t} t CROSS JOIN {w} w CROSS JOIN {m} m CROSS JOIN {j} j
        CROSS JOIN {route} route CROSS JOIN {authority} authority CROSS JOIN {native} native
        CROSS JOIN {source} source CROSS JOIN {binding} binding
        CROSS JOIN LATERAL (SELECT convert_from(j.input_bytes,'UTF8')::jsonb AS body) request
        CROSS JOIN LATERAL (SELECT convert_from(j.result_bytes,'UTF8')::jsonb AS body) result
        CROSS JOIN LATERAL (SELECT j.audit_json->'before'->0 AS before,j.audit_json->'after'->0 AS after,
                                   j.audit_json->'transactions'->0 AS audit_txn) audit;
        IF valid IS DISTINCT FROM true THEN
            RAISE EXCEPTION 'rma-stage-stock-candidate-graph-rejected';
        END IF;
    END
    $stock_adjust_graph$;
    """


def allocator_sql():
    tokens = " UNION ALL ".join(f'SELECT "RowVersion" AS token FROM {temporary(part)}' for part in ROWVERSIONS)
    value = "CASE WHEN octet_length(token)=8 THEN ('x'||encode(token,'hex'))::bit(64)::bigint END"
    return f"""
    -- START is already 1. Acquire both allocation locks without taking an identity.
    ALTER SEQUENCE {CORE_SEQUENCE} START WITH 1;
    ALTER SEQUENCE {MOVEMENT_SEQUENCE} START WITH 1;
    DO $stock_adjust_allocators$
    DECLARE
        kept record;
        originals bytea[];
        highest numeric;
        used_max numeric;
        duplicate boolean;
        core_floor numeric;
        movement_floor numeric;
        core_next numeric;
        movement_next numeric;
    BEGIN
        SELECT array_agg(token),max(value) INTO originals,highest
          FROM (SELECT token,{value} AS value FROM ({tokens}) rows) typed
          HAVING count(DISTINCT value)=3 AND min(value)>0;
        IF originals IS NULL THEN RAISE EXCEPTION 'rma-stage-stock-candidate-token-rejected'; END IF;

        -- The same generator is shared by all Core token tables, including retained tenants.
        FOR kept IN SELECT DISTINCT n.nspname,c.relname FROM pg_trigger t
          JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace
          WHERE t.tgfoid='{TOKEN_FUNCTION}'::regprocedure AND t.tgtype=23 AND NOT t.tgisinternal
          ORDER BY n.nspname,c.relname
        LOOP
          EXECUTE format('SELECT max(CASE WHEN octet_length("RowVersion")=8 THEN
              (''x''||encode("RowVersion",''hex''))::bit(64)::bigint END),
              bool_or("RowVersion"=ANY($1)) FROM %I.%I',kept.nspname,kept.relname)
              INTO used_max,duplicate USING originals;
          IF duplicate THEN RAISE EXCEPTION 'rma-stage-stock-candidate-token-rejected'; END IF;
          highest := greatest(highest,used_max);
        END LOOP;
        SELECT last_value::numeric+is_called::integer INTO STRICT core_floor FROM {CORE_SEQUENCE};
        SELECT last_value::numeric+is_called::integer INTO STRICT movement_floor FROM {MOVEMENT_SEQUENCE};
        core_next := greatest(core_floor,highest+1);
        movement_next := greatest(movement_floor,
            (SELECT max(sequence)::numeric+1 FROM {relation(EFFECTS[3])}),
            (SELECT sequence::numeric+1 FROM {temporary(EFFECTS[3])}));
        IF core_next>9223372036854775807 OR movement_next>9223372036854775807 THEN
            RAISE EXCEPTION 'rma-stage-stock-candidate-token-rejected';
        END IF;
        IF core_next>core_floor THEN EXECUTE format('ALTER SEQUENCE {CORE_SEQUENCE} RESTART WITH %s',core_next); END IF;
        IF movement_next>movement_floor THEN EXECUTE format('ALTER SEQUENCE {MOVEMENT_SEQUENCE} RESTART WITH %s',movement_next); END IF;
        PERFORM set_config('{PLAN}_core_next',core_next::text,true);
        PERFORM set_config('{PLAN}_movement_next',movement_next::text,true);
    END
    $stock_adjust_allocators$;
    """


def insert(part):
    destination, names = relation(part), columns(part)
    override = " OVERRIDING SYSTEM VALUE" if part is EFFECTS[3] else ""
    write = f"INSERT INTO {destination} ({names}){override} SELECT {names} FROM {temporary(part)};"
    if part not in ROWVERSIONS:
        return write
    return f"""ALTER TABLE ONLY {destination} DISABLE TRIGGER cp6_rowversion_v1;
    {write}
    ALTER TABLE ONLY {destination} ENABLE TRIGGER cp6_rowversion_v1;"""


def adopt_sql():
    writes = "\n".join(map(insert, WRITES))
    s, t, _, m, j, _, _, native, _, _ = map(temporary, WRITES)
    return writes + f"""
    CREATE SCHEMA {RECEIPTS};
    REVOKE ALL ON SCHEMA {RECEIPTS} FROM PUBLIC;
    CREATE TABLE {RECEIPTS}.receipt (
        singleton boolean PRIMARY KEY CHECK(singleton), input jsonb NOT NULL, result_json text NOT NULL);
    REVOKE ALL ON {RECEIPTS}.receipt FROM PUBLIC;
    INSERT INTO {RECEIPTS}.receipt
    SELECT true,p->'key',json_build_object('outcome','IMPORTED','recoveryReady',false,'profile','{PROFILE}',
        'binding',p->'key','operationId',j.operation_id,'stockId',s."Id",'transactionId',t."Id",
        'transactionNo',t."TxnNo"::text,'nativeMessageId',n."Id",'messageId',n."MessageId",
        'movementSequence',m.sequence,'coreRowVersionNext',current_setting('{PLAN}_core_next')::bigint,
        'movementSequenceNext',current_setting('{PLAN}_movement_next')::bigint)::text
    FROM (SELECT current_setting('{PLAN}')::jsonb p) plan CROSS JOIN {s} s CROSS JOIN {t} t
         CROSS JOIN {m} m CROSS JOIN {j} j CROSS JOIN {native} n;
    SET CONSTRAINTS {STOCK.SCHEMA}.stock_source_proof IMMEDIATE;
    SELECT result_json FROM {RECEIPTS}.receipt;
    """


def checked_effects(source, output, tenant, manifest_pin, effects_pin):
    manifest_name = "manifest.json"
    names = {manifest_name, *(part.member for part in EFFECTS)}
    if {path.name for path in source.iterdir()} != names:
        raise stage.StageError("rma-stage-stock-effect-members-rejected")
    checked = output / "effects"
    checked.mkdir(mode=0o700)
    for name in names:
        path = source / name
        assets.entry(path, stat.S_ISREG, "rma-stage-stock-effect-members-rejected")
        shutil.copyfile(path, checked / name, follow_symlinks=False)
    if archive.file_digest(checked / manifest_name)["sha256"] != effects_pin:
        raise stage.StageError("rma-stage-stock-effect-digest-mismatch")
    manifest = json.loads((checked / manifest_name).read_bytes())
    if not isinstance(manifest, dict) or set(manifest) != {"format", "tenantId", "operationId", "sourceManifestSha256", "members"} \
            or manifest["format"] != EFFECT_FORMAT or manifest["tenantId"] != str(tenant) \
            or manifest["sourceManifestSha256"] != manifest_pin:
        raise stage.StageError("rma-stage-stock-effect-binding-rejected")
    if manifest["members"] != {part.member: archive.file_digest(checked / part.member) for part in EFFECTS}:
        raise stage.StageError("rma-stage-stock-effect-digest-mismatch")
    return archive.guid(manifest["operationId"])


def execute(args):
    pg = args.pg_bin.resolve(strict=True)
    target = archive.PhysicalTarget.from_mapping(json.loads(args.target_file.read_text(encoding="utf-8-sig")))
    tenant, candidate_id = archive.guid(args.tenant), archive.guid(args.candidate_id)
    pin, effect_pin = stage.digest_pin(args.manifest_sha256), stage.digest_pin(args.effects_sha256)
    if not args.username or "\0" in args.username:
        raise stage.StageError("rma-stage-invalid-username")
    recover = args.action == "recover"
    output = paths.checked_output(args.output.absolute(), args.target_file.resolve(strict=True))
    if not recover:
        bundle, effects, root = (assets.checked_root(path.absolute()).resolve(strict=True)
                                 for path in (args.bundle, args.effects, args.file_content_root))
        for source in (bundle, effects, root):
            paths.checked_output(output, source)
    archive.private_directory(output)
    plan = {"key": {"target": target.mapping(), "candidateId": str(candidate_id), "tenantId": str(tenant),
                    "manifestSha256": pin, "effectsSha256": effect_pin}}
    receipt = candidate.receipt_sql(schema=RECEIPTS, profile=PROFILE)
    if recover:
        driver = candidate.target_sql(plan, True) + closed_sql() + receipt
    else:
        _, metadata = stage.checked_bundle(bundle, output, tenant, pin, target, root, include_stock_outbox=True,
                                           include_file_content_history=True)
        section = metadata["stockOutbox"]
        if section["root"]["epoch"] != 1:
            raise stage.StageError("rma-stage-stock-candidate-fixed-epoch-required")
        if any(section["tables"][archive.STOCK_MEMBER_PREFIX + part.member]["count"] != int(part in WRITES)
               for part in STOCK.TABLES):
            raise stage.StageError("rma-stage-stock-candidate-shape-rejected")
        operation = checked_effects(effects, output, tenant, pin, effect_pin)
        plan.update(operationId=str(operation), root=section["root"]["id"], lockKey=section["root"]["lockKey"])
        driver = candidate.target_sql(plan, False) + closed_sql() + rf"""
        SELECT to_regnamespace('{RECEIPTS}') IS NOT NULL AS has_receipt \gset
        \if :has_receipt
        {receipt}
        \else
        {layout_sql()}
        {stage_sql()}
        {graph_sql()}
        {allocator_sql()}
        {adopt_sql()}
        \endif
        """
    (output / "driver.sql").write_text(driver, encoding="utf-8")
    return stage.run_driver(pg, target, args.username, output, "recover" if recover else "stage")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    for name in ("import", "recover"):
        action = actions.add_parser(name)
        for field in ("pg-bin", "target-file", "output"):
            action.add_argument("--" + field, type=Path, required=True)
        for field in ("username", "tenant", "candidate-id", "manifest-sha256", "effects-sha256"):
            action.add_argument("--" + field, required=True)
        if name == "import":
            for field in ("bundle", "effects", "file-content-root"):
                action.add_argument("--" + field, type=Path, required=True)
    try:
        sys.stdout.buffer.write(execute(parser.parse_args()))
    except (archive.ArchiveError, OSError, ValueError, csv.Error, stage.StageError, subprocess.SubprocessError) as error:
        print("Stock adjustment candidate rejected:", error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
