#!/usr/bin/env python3
"""Recover missing Core RMA rows on their original source, retaining current shared authority.

The supported recovery unit is the adopted v11 Core RMA archive and its same-cut
companions. This command does not replace a database/schema, rewind live rows,
restore credentials, or dispatch archived messages. A missing shared dependency
is a closed recovery, not permission to manufacture another Owner's effects.
"""

import argparse
import csv
import json
import os
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
import archive
import candidate_core
import candidate_journal
import erp_integration as erp
import erp_terminal_candidate
import file_content_assets as assets
import file_content_candidate
import file_content_history as history
import iam_v6 as iam
import identity_priority as identity
import privacy_evidence as privacy
import reconcile_erp_deliveries as erp_delivery
import reconcile_stock_outbox as stock_delivery
import source_v3
import stage
import stock_adjust_candidate as stock_effects
import visibility_v4 as visibility


PROFILE = "cp6-rma-live-missing-rows-recovery-v1"
SCHEMA = "cp6_rma_live_recovery_v1"
RECEIPTS = SCHEMA + ".operations"
Q = archive.literal


class RecoveryError(RuntimeError):
    pass


def require(condition, reason):
    if not condition:
        raise RecoveryError("rma-live-" + reason)


def names(columns):
    return ",".join('"' + column + '"' for column in columns)


def local_path(path):
    value = path.resolve(strict=True).as_posix()
    require(not any(ord(char) < 32 for char in value) and "'" not in value,
            "native-path-rejected")
    return "'" + value + "'"


def source_target(query, target, *, write):
    actual = query(f"""
        BEGIN ISOLATION LEVEL READ COMMITTED {'READ WRITE' if write else 'READ ONLY'};
        SET LOCAL search_path=pg_catalog;
        SET LOCAL standard_conforming_strings=on;
        SET LOCAL lock_timeout='10s';
        SET LOCAL statement_timeout='30s';
        SET LOCAL idle_in_transaction_session_timeout='150s';
        {archive.JOURNAL_COPY_SETTINGS}
        SELECT json_build_object('target',json_build_object(
          'Server',host(inet_server_addr()),'Port',inet_server_port(),'Database',d.datname,
          'DatabaseOid',d.oid::bigint,'SystemIdentifier',p.system_identifier::text),
          'allowed',NOT pg_is_in_recovery() AND NOT d.datistemplate AND d.datallowconn
            AND session_user=current_user AND d.datdba=r.oid AND r.rolsuper,
          'candidate',coalesce(shobj_description(d.oid,'pg_database'),'') LIKE
            '{candidate_core.MARKER}%')
        FROM pg_database d CROSS JOIN pg_control_system() p
        JOIN pg_roles r ON r.rolname=current_user WHERE d.datname=current_database();
    """)
    require(actual == dict(target=target.mapping(), allowed=True, candidate=False), "source-identity-rejected")


def commission(path, target):
    installation, digest = candidate_core.commissioning_pin(path, target)
    return archive.guid(installation), digest


def candidate_proof(args, target, tenant, installation, commission_digest, pin, effects_pin, output):
    expected = (
        (candidate_core.RECEIPTS + ".receipt", candidate_core.PROFILE),
        (candidate_core.RECEIPTS + "." + candidate_journal.RECEIPT, candidate_journal.PROFILE),
        (file_content_candidate.RECEIPTS + ".receipt", file_content_candidate.PROFILE),
        (erp_terminal_candidate.RECEIPTS + ".receipt", erp_terminal_candidate.PROFILE),
        (stock_effects.RECEIPTS + ".receipt", stock_effects.PROFILE),
    )
    logs = output / "candidate"
    logs.mkdir()
    evidence = {}
    with archive.snapshot_session(args.pg_bin, archive.connection_args(target, args.username), os.environ, logs) as query:
        archive.check_physical_source(query, target)
        trusted = query(f"""
            SELECT to_json(session_user=current_user AND d.datdba=r.oid AND r.rolsuper
              AND shobj_description(d.oid,'pg_database')={Q(candidate_core.MARKER + str(args.candidate_id))}
              AND EXISTS (SELECT FROM cp6_rma_core_commission_v1.installation c
                WHERE c.singleton AND c.commission_receipt::jsonb->>'InstallationId'={Q(installation)}
                  AND encode(sha256(convert_to(c.commission_receipt,'UTF8')),'hex')={Q(commission_digest)}))
            FROM pg_database d JOIN pg_roles r ON r.rolname=current_user WHERE d.datname=current_database();
        """)
        require(trusted, "candidate-commission-rejected")
        for relation, profile in expected:
            proof = query(f"""
                SELECT coalesce((SELECT json_build_object('input',input,'result',result_json::jsonb,
                  'sha256',encode(sha256(convert_to(result_json,'UTF8')),'hex')) FROM {relation} WHERE singleton),'null'::json);
            """)
            require(proof is not None, "candidate-receipt-missing")
            key, result = proof["input"], proof["result"]
            require(key.get("target") == target.mapping() and key.get("tenantId") == str(tenant)
                    and key.get("candidateId") == str(args.candidate_id) and key.get("manifestSha256") == pin
                    and result.get("binding") == key and result.get("outcome") == "IMPORTED"
                    and result.get("profile") == profile and result.get("recoveryReady") is False,
                    "candidate-receipt-rejected")
            if profile == stock_effects.PROFILE:
                require(key.get("effectsSha256") == effects_pin, "candidate-effects-rejected")
            evidence[profile] = proof["sha256"]
    return evidence


def receipt_table(query, *, create):
    exists = query(f"SELECT to_json(to_regnamespace('{SCHEMA}') IS NOT NULL);")
    if not exists:
        if not create:
            return False
        query(f"""
            CREATE SCHEMA {SCHEMA};
            REVOKE ALL ON SCHEMA {SCHEMA} FROM PUBLIC;
            CREATE TABLE {RECEIPTS} (
              tenant_id uuid NOT NULL, operation_id uuid NOT NULL, input jsonb NOT NULL, result_json text NOT NULL,
              PRIMARY KEY(tenant_id,operation_id));
            REVOKE ALL ON {RECEIPTS} FROM PUBLIC;
            CREATE FUNCTION {SCHEMA}.immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $body$
              BEGIN RAISE EXCEPTION 'RMA_LIVE_RECOVERY_RECEIPT_IMMUTABLE' USING ERRCODE='23514'; END $body$;
            REVOKE ALL ON FUNCTION {SCHEMA}.immutable() FROM PUBLIC;
            CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON {RECEIPTS}
              FOR EACH STATEMENT EXECUTE FUNCTION {SCHEMA}.immutable();
            SELECT to_json(true);
        """)
    valid = query(f"""
        SELECT to_json(EXISTS (SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
          WHERE c.oid=to_regclass('{RECEIPTS}') AND c.relkind='r' AND c.relowner=current_user::regrole
            AND n.nspowner=c.relowner AND NOT c.relrowsecurity
            AND NOT EXISTS(SELECT FROM aclexplode(c.relacl) a WHERE a.grantee<>c.relowner)
            AND NOT EXISTS(SELECT FROM aclexplode(n.nspacl) a WHERE a.grantee<>n.nspowner)
            AND (SELECT array_agg(attname::text||':'||atttypid::regtype::text ORDER BY attnum)
              FROM pg_attribute WHERE attrelid=c.oid AND attnum>0 AND NOT attisdropped)=
              ARRAY['tenant_id:uuid','operation_id:uuid','input:jsonb','result_json:text']
            AND EXISTS(SELECT FROM pg_trigger WHERE tgrelid=c.oid AND tgname='immutable'
              AND tgenabled IN ('O','A') AND tgfoid='{SCHEMA}.immutable()'::regprocedure)));
    """)
    require(valid, "receipt-layout-rejected")
    return True


def read_receipt(query, tenant, operation, key=None):
    record = query(f"""
        SELECT coalesce((SELECT json_build_object('input',input,'result',result_json)
          FROM {RECEIPTS} WHERE tenant_id={Q(tenant)}::uuid AND operation_id={Q(operation)}::uuid),'null'::json);
    """)
    if record is None:
        return None
    require(key is None or record["input"] == key, "operation-conflict")
    return record["result"]


def binding(query, target, tenant, installation, receipt_digest, manifest):
    schema = "t_" + tenant.hex
    rows = query(f"""
        SELECT coalesce((SELECT json_build_object('ownerOid',d.datdba::bigint,'roleOid',r.oid::bigint,
          'migration',json_build_object('operationId',m.operation_id,'migrationId',m.migration_id,
            'scriptSha256',m.script_sha256,'receiptSha256',encode(sha256(convert_to(m.result_json,'UTF8')),'hex')))
          FROM cp6_rma_tenant_v1.installation i CROSS JOIN cp6_rma_core_commission_v1.installation c
          CROSS JOIN pg_database d JOIN cp6_rma_tenant_v1.tenant_migrations m ON m.tenant_id={Q(tenant)}::uuid
          JOIN cp6_tenancy_v1.login_tenant l ON (l.tenant_id,l.role_oid,l.role_name)=(m.tenant_id,m.role_oid,m.role_name)
          JOIN pg_roles r ON (r.oid,r.rolname)=(l.role_oid,l.role_name)
          JOIN {schema}.{archive.HISTORY} h ON (h.tenant_id,h.lineage)=(m.tenant_id,m.lineage)
          WHERE i.singleton AND c.singleton AND d.datname=current_database()
            AND i.installation_id={Q(installation)}::uuid AND i.profile={Q(archive.PROFILE)}
            AND i.target={Q(json.dumps(target.mapping()))}::jsonb
            AND encode(sha256(convert_to(c.commission_receipt,'UTF8')),'hex')={Q(receipt_digest)}
            AND m.lineage={Q(archive.LINEAGE)} AND m.state='Ready' AND m.migration_id={Q(archive.MIGRATION)}
            AND (m.operation_id,m.migration_id,m.script_sha256,m.result_json)=
                (h.operation_id,h.migration_id,h.script_sha256,h.result_json)
            AND r.rolcanlogin AND NOT(r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
            AND NOT EXISTS(SELECT FROM pg_auth_members WHERE member=r.oid OR roleid=r.oid)
          FOR UPDATE OF m), 'null'::json);
    """)
    require(rows is not None and rows["migration"] == manifest["migration"], "current-binding-rejected")
    archive.require_v1_schema(query, tenant)
    query(f"LOCK TABLE {schema}.heads,{schema}.versions IN ACCESS EXCLUSIVE MODE; SELECT to_json(true);")
    require(query(f"""
        SELECT to_json(current_setting('session_replication_role')='origin'
          AND (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE c.oid IN ('{schema}.heads'::regclass,'{schema}.versions'::regclass,
              '{schema}.{archive.HISTORY}'::regclass) AND c.relkind='r' AND c.relowner={rows['ownerOid']}::oid
              AND n.nspowner=c.relowner)=3
          AND EXISTS(SELECT FROM pg_trigger WHERE tgrelid='{schema}.versions'::regclass
            AND tgname='rma_application_immutable' AND tgenabled IN ('O','A'))
          AND EXISTS(SELECT FROM pg_trigger WHERE tgrelid='{schema}.versions'::regclass
            AND tgname='rma_application_no_truncate' AND tgenabled IN ('O','A'))
          AND NOT EXISTS(SELECT FROM pg_constraint WHERE conrelid IN
            ('{schema}.heads'::regclass,'{schema}.versions'::regclass) AND NOT convalidated));
    """), "current-layout-rejected")
    archive.check_original_journal(query, target, installation, rows, archive.original_journal_rows(tenant))
    return rows


def copy_temp(query, label, path, select):
    table = "pg_temp.live_" + label
    count = query(rf"""
        CREATE TEMP TABLE live_{label} ON COMMIT DROP AS {select} WITH NO DATA;
        \copy {table} FROM {local_path(path)} WITH (FORMAT csv, HEADER MATCH)
        SELECT to_json(count(*)) FROM {table};
    """)
    return table, count


def lock_rows(query, relation, predicate, mode="SHARE"):
    return query(f"""WITH locked AS MATERIALIZED (SELECT 1 FROM {relation} WHERE {predicate} FOR {mode})
        SELECT to_json(count(*)) FROM locked;""")


def exact(query, label, path, select, *, complete=False):
    table, count = copy_temp(query, label, path, select)
    comparison = f"""SELECT to_json(NOT EXISTS (SELECT FROM {table} wanted WHERE NOT EXISTS
        (SELECT FROM ({select}) live WHERE to_jsonb(live)=to_jsonb(wanted)))"""
    if complete:
        comparison += f" AND (SELECT count(*) FROM ({select}) live)={count}"
    require(query(comparison + ");"), "shared-original-missing-or-changed-" + label)
    return count


def restore_rows(query, output, tenant, metadata):
    schema = "t_" + tenant.hex
    query(rf"""
        CREATE TEMP TABLE live_heads (LIKE {schema}.heads INCLUDING CONSTRAINTS) ON COMMIT DROP;
        CREATE TEMP TABLE live_versions (LIKE {schema}.versions INCLUDING CONSTRAINTS) ON COMMIT DROP;
        \i {local_path(output / 'rma-rows.sql')}
        SELECT to_json(true);
    """)
    for table in ("heads", "versions"):
        count = query(f"SELECT to_json(count(*)) FROM pg_temp.live_{table};")
        require(count == metadata["counts"][table], "archive-count-rejected")
        require(query(f"""SELECT to_json(NOT EXISTS(SELECT FROM {schema}.{table} live WHERE NOT EXISTS
            (SELECT FROM pg_temp.live_{table} wanted WHERE to_jsonb(wanted)=to_jsonb(live))));"""),
                "live-rows-newer-or-divergent")
    inserted = {}
    for table, columns in candidate_core.COPY_COLUMNS.items():
        key = "wanted.tenant_id=live.tenant_id AND wanted.id=live.id"
        if table == "versions":
            key += " AND wanted.version=live.version"
        inserted[table] = query(f"""WITH inserted AS (
            INSERT INTO {schema}.{table} ({columns}) SELECT {columns} FROM pg_temp.live_{table} wanted
            WHERE NOT EXISTS(SELECT FROM {schema}.{table} live WHERE {key}) RETURNING 1)
            SELECT to_json(count(*)) FROM inserted;""")
    query("SET CONSTRAINTS ALL IMMEDIATE; SELECT to_json(true);")
    return inserted


def keep_iam(query, tenant, bundle):
    current = iam.rows_sql(tenant, archive.original_journal_rows(tenant), visibility.rows_sql(tenant))
    table, _ = copy_temp(query, "iam", bundle / iam.MEMBER, current)
    # Revoked grants and deleted/disabled principals remain revoked. Stable historical IDs
    # are never reassigned to another tenant or replaced from backup.
    require(query(f"""SELECT to_json(NOT EXISTS(SELECT FROM {table} old JOIN public."Sys_Users" u
        ON u."Id"=CASE WHEN old.kind='participant' THEN old.id::uuid END WHERE u."TenantId"<>old.tenant_id)
        AND EXISTS(SELECT FROM public."Sys_Tenants" WHERE "Id"={Q(tenant)}::uuid));"""), "iam-identity-reassigned")
    return query(f"""WITH current AS ({current}) SELECT json_build_object(
        'unchanged',(SELECT count(*) FROM {table} old JOIN current c USING(kind,tenant_id,id) WHERE old.facts=c.facts),
        'changed',(SELECT count(*) FROM {table} old JOIN current c USING(kind,tenant_id,id) WHERE old.facts<>c.facts),
        'currentOnly',(SELECT count(*) FROM current c WHERE NOT EXISTS(SELECT FROM {table} old
          WHERE (old.kind,old.tenant_id,old.id)=(c.kind,c.tenant_id,c.id))),
        'revokedOrAbsent',(SELECT count(*) FROM {table} old WHERE NOT EXISTS(SELECT FROM current c
          WHERE (old.kind,old.tenant_id,old.id)=(c.kind,c.tenant_id,c.id))));""")


def keep_identity(query, tenant, bundle):
    mutable = {
        "Cp6_OutboxMessage": {"Status", "AttemptCount", "AvailableAtUtc", "LeaseOwner", "LeaseToken",
            "LeaseExpiresAtUtc", "PublishedAtUtc", "DeadLetteredAtUtc", "LastErrorCode", "SupportReference", "RowVersion"},
        "Cp6_InboxMessage": {"Status", "AttemptCount", "OutcomeCode", "LastErrorCode", "SupportReference", "ProcessedAtUtc", "RowVersion"},
        "Cp6_InboxAggregateCheckpoint": {"AggregateVersion", "UpdatedAtUtc", "RowVersion"},
        "Cp6_DeadLetterRecord": {"ReplayedAtUtc", "ReplayReasonCode", "RowVersion"},
    }
    total = 0
    for index, part in enumerate(identity.TABLES):
        relation = identity.relation(part)
        lock_rows(query, relation, identity.tenant_filter(tenant))
        table, count = copy_temp(query, "identity_" + str(index), bundle / part.member, identity.rows_sql(part, tenant))
        kept = [column for column in part.columns if column not in mutable[part.name]]
        left = "jsonb_build_array(" + ",".join("live." + names([col]) for col in kept) + ")"
        right = "jsonb_build_array(" + ",".join("old." + names([col]) for col in kept) + ")"
        require(query(f"""SELECT to_json(NOT EXISTS(SELECT FROM {table} old WHERE NOT EXISTS
            (SELECT FROM {relation} live WHERE {left}={right})));"""), "identity-original-missing-or-changed")
        total += count
    return total


def keep_erp(query, tenant, bundle, metadata):
    report = []
    # C03 handlers hold CommandInbox before updating Aggregate. Take the same
    # order so a live handler can finish before we compare its retained facts.
    lock_order = (erp_delivery.COMMAND,) + tuple(part for part in erp.TABLES if part != erp_delivery.COMMAND)
    for part in lock_order:
        lock_rows(query, erp.relation(part, erp.SCHEMA), erp.tenant_filter(tenant))
    for index, part in enumerate(erp.TABLES):
        if part in erp_delivery.TEXT_BINDINGS:
            bindings = list(erp_delivery.archived_rows(bundle / part.member, part, tenant,
                metadata["erpIntegrationCounts"][part.name]))
            if bindings:
                current = erp_delivery.lookup(query, part, tenant,
                    [dict(key, LeaseTokenSha256=lease) for key, _, lease in bindings])
                require(len(current) == len(bindings) and all(row["decision"] == "PRESERVE_CURRENT" for row in current),
                        "erp-delivery-unresolved")
                report.extend(dict(table=part.name, **row) for row in current)
        else:
            exact(query, "erp_" + str(index), bundle / part.member, erp.rows_sql(part, tenant))
    return report


def keep_stock(query, tenant, bundle, metadata, output):
    stock, route = stock_effects.STOCK, metadata["stockOutbox"]["root"]
    lock_rows(query, 'public."Sys_Tenants"', f'"Id"={Q(tenant)}::uuid', "UPDATE")
    lock_rows(query, stock.relation(stock.AUTHORITY), f'"RootId"={Q(route["id"])}', "UPDATE")
    query(f'DO $lock$ BEGIN PERFORM pg_advisory_xact_lock({int(route["lockKey"])}); END $lock$; SELECT to_json(true);')
    lock_rows(query, stock.relation(stock.ROUTE_TABLE), f'"Id"={Q(route["id"])}', "UPDATE")
    control = stock_delivery.current_route(query, stock, tenant, route)
    require(control is not None and control["eligible"] and control["epoch"] == route["epoch"]
            and control["phase"] == 0 and control["lastSwitchId"] == route.get("lastSwitchId")
            and (control["epoch"] == 1 or control["lastAdvance"] is not None),
            "stock-current-route-incompatible")
    originals = list(stock_delivery.archived_rows(stock, bundle, tenant, route))
    current = stock_delivery.lookup(query, stock, tenant, route,
        [dict(key, LeaseTokenSha256=lease) for key, _, lease in originals]) if originals else []
    require(len(current) == len(originals) and all(row["decision"] == "PRESERVE_CURRENT" for row in current),
            "stock-delivery-or-epoch-unresolved")
    for index, effect in enumerate(stock_effects.EFFECTS):
        column = "tenant_id" if index == 3 else "TenantId"
        lock_rows(query, effect.relation, f'{names([column])}={Q(tenant)}::uuid')
        exact(query, "stock_effect_" + str(index), output / "effects" / effect.member,
              f'SELECT {names(effect.columns)} FROM {effect.relation} WHERE {names([column])}={Q(tenant)}::uuid')
    # The native lease state was reconciled above. Immutable companion receipts and
    # audit rows present in the old cut must still exist; newer records are retained.
    for index, part in enumerate(stock.TABLES):
        if part in (stock.AUTHORITY, stock.ROUTE_TABLE, stock.NATIVE, stock.BINDING, stock.RUN, stock.DEAD_LETTER):
            continue
        columns = [name for name, _ in part.fields]
        relation = stock.relation(part)
        exact(query, "stock_original_" + str(index), bundle / stock_delivery.member(part),
              f'SELECT {names(columns)} FROM {relation}')
    return current


def companions(query, args, tenant, metadata, output):
    bundle = output / "bundle"
    counts = {}
    # Readers share the same current authority root as the native Owners. Lock the
    # retained mutable source rows too, including the paths whose writer uses its
    # own root. This pins only A; metadata/rows for B are never replaced.
    for relation, column in (
        ('public."T_OutboundOrder"', 'TenantId'), ('public."T_OutboundOrderDetail"', 'TenantId'),
        ('public."T_Order"', 'TenantId'), ('public."T_WorkOrder"', 'TenantId'),
        ('public."T_Location"', 'TenantId'), ('cp6_outbound_lineage_v1.order_lines', 'tenant_id'),
        *((part.relation, 'tenant_id') for part in history.MEMBERS.values()),
    ):
        lock_rows(query, relation, f'{names([column])}={Q(tenant)}::uuid')
    counts["rmaOriginals"] = exact(query, "rma_originals", bundle / archive.JOURNAL_MEMBER,
        archive.original_journal_rows(tenant))
    for part in source_v3.slices(tenant):
        counts["source_" + part.key] = exact(query, "source_" + part.key, bundle / part.member,
                                            part.query, complete=True)
    counts["visibility"] = exact(query, "visibility", bundle / visibility.MEMBER, visibility.rows_sql(tenant), complete=True)
    for index, (key, part) in enumerate(history.MEMBERS.items()):
        counts["file_" + key] = exact(query, "file_" + str(index), bundle / history.filename(key), history.rows_sql(tenant)[key])
    counts["fileAssets"] = exact(query, "file_assets", bundle / assets.MEMBER, assets.rows_sql(tenant))
    for index, part in enumerate(privacy.TABLES):
        counts["privacy_" + str(index)] = exact(query, "privacy_" + str(index), bundle / part.member,
            privacy.rows_sql(part, tenant, metadata["privacyEvidence"]["environmentId"]))
    identity_count = keep_identity(query, tenant, bundle)
    iam_result = keep_iam(query, tenant, bundle)
    erp_result = keep_erp(query, tenant, bundle, metadata)
    stock_result = keep_stock(query, tenant, bundle, metadata, output)
    return dict(originalRows=counts, currentIam=iam_result, identityOriginals=identity_count,
                erpDeliveries=erp_result, stockDeliveries=stock_result)


def prepare_rows(args, output, tenant):
    candidate_core.emit_rows(args.pg_bin, output / "bundle", output, "t_" + tenant.hex)
    content = (output / "tenant-rows.sql").read_bytes()
    for table, columns in candidate_core.COPY_COLUMNS.items():
        original = f"COPY t_{tenant.hex}.{table} ({columns}) FROM stdin;".encode("ascii")
        replacement = f"COPY pg_temp.live_{table} ({columns}) FROM stdin;".encode("ascii")
        # emit_rows already accepts exactly these native COPY headers and no restore preamble.
        content = content.replace(original + b"\r\n", replacement + b"\r\n").replace(original + b"\n", replacement + b"\n")
    (output / "rma-rows.sql").write_bytes(content)


def _execute(args):
    args.pg_bin = args.pg_bin.resolve(strict=True)
    target = archive.PhysicalTarget.from_mapping(json.loads(args.target_file.read_text(encoding="utf-8-sig")))
    tenant, operation = archive.guid(args.tenant), archive.guid(args.operation)
    require(bool(args.username) and "\0" not in args.username, "username-rejected")
    output = history.checked_output(args.output.absolute(), args.target_file.resolve(strict=True))
    recovering = args.action == "recover"
    if not recovering:
        args.candidate_id = archive.guid(args.candidate_id)
        other = archive.PhysicalTarget.from_mapping(json.loads(args.candidate_target_file.read_text(encoding="utf-8-sig")))
        bundle, root, effects = (assets.checked_root(path.absolute()).resolve(strict=True)
                                for path in (args.bundle, args.file_content_root, args.effects))
        for path in (bundle, root, effects):
            history.checked_output(output, path)
    archive.private_directory(output)
    key = None
    if not recovering:
        pin, effects_pin = stage.digest_pin(args.manifest_sha256), stage.digest_pin(args.effects_sha256)
        _, metadata = stage.checked_bundle(bundle, output, tenant, pin, other, root,
                                           include_stock_outbox=True, include_file_content_history=True)
        manifest = json.loads((output / "bundle" / "manifest.json").read_bytes())
        require(archive.PhysicalTarget.from_mapping(manifest["source"]) == target, "archive-source-mismatch")
        installation, source_pin = commission(args.commission_receipt, target)
        require(str(installation) == manifest["installationId"], "archive-installation-mismatch")
        candidate_installation, candidate_pin = commission(args.candidate_commission_receipt, other)
        stock_effects.checked_effects(effects, output, tenant, pin, effects_pin)
        staging = candidate_proof(args, other, tenant, candidate_installation, candidate_pin, pin, effects_pin, output)
        prepare_rows(args, output, tenant)
        key = dict(profile=PROFILE, target=target.mapping(), tenantId=str(tenant), operationId=str(operation),
                   manifestSha256=pin, effectsSha256=effects_pin, sourceCommissionSha256=source_pin,
                   candidateTarget=other.mapping(), candidateId=str(args.candidate_id),
                   candidateCommissionSha256=candidate_pin, stagingReceipts=staging)
    logs = output / "native"
    logs.mkdir()
    committed = False
    with (output / "transaction.sql").open("x", encoding="utf-8", newline="\n") as transcript:
        with archive.snapshot_session(args.pg_bin, archive.connection_args(target, args.username), os.environ, logs) as native:
            def query(sql, failure="rma-live-native-rejected (see native logs)"):
                transcript.write(sql + "\n")
                transcript.flush()
                return native(sql, failure=failure)

            source_target(query, target, write=not recovering)
            if recovering:
                require(receipt_table(query, create=False), "receipt-not-found")
                result = read_receipt(query, tenant, operation)
                require(result is not None, "receipt-not-found")
            else:
                query(f"""DO $gate$ BEGIN
                    PERFORM cp6_authentication_v1.acquire();
                    END $gate$; SELECT to_json(true);""")
                receipt_table(query, create=True)
                result = read_receipt(query, tenant, operation, key)
                if result is None:
                    binding(query, target, tenant, installation, source_pin, manifest)
                    inserted = restore_rows(query, output, tenant, metadata)
                    kept = companions(query, args, tenant, metadata, output)
                    result = query(f"""
                        SELECT json_build_object('outcome','RESTORED','profile',{Q(PROFILE)},
                          'tenantId',{Q(tenant)},'operationId',{Q(operation)},'binding',{Q(json.dumps(key))}::json,
                          'inserted',{Q(json.dumps(inserted))}::json,'retained',{Q(json.dumps(kept))}::json,
                          'snapshot',{Q(json.dumps(manifest['snapshot']))}::json,
                          'currentWatermark',pg_current_snapshot()::text,'committedAt',clock_timestamp(),
                          'automaticSends',0,'recoveryReady',true)::text;
                    """)
                    # The query helper decodes JSON; persist one canonical result exactly once.
                    result = json.dumps(result, separators=(",", ":"), ensure_ascii=False)
                    query(f"""INSERT INTO {RECEIPTS}(tenant_id,operation_id,input,result_json)
                        VALUES({Q(tenant)}::uuid,{Q(operation)}::uuid,{Q(json.dumps(key))}::jsonb,{Q(result)});
                        SET CONSTRAINTS ALL IMMEDIATE; SELECT to_json(true);""")
        committed = True
    require(committed, "outcome-unknown-use-recover")
    (output / "result.json").write_text(result + "\n", encoding="utf-8")
    return result


def execute(args):
    # Native bytea is hex in CSV. Keep the same accepted field size as the
    # existing ERP/Stock reconciliation tools, including after bundle validation.
    previous = csv.field_size_limit(2_147_483_647)
    try:
        return _execute(args)
    finally:
        csv.field_size_limit(previous)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    for action in ("apply", "recover"):
        command = actions.add_parser(action)
        for name in ("pg-bin", "target-file", "output"):
            command.add_argument("--" + name, type=Path, required=True)
        for name in ("tenant", "operation", "username"):
            command.add_argument("--" + name, required=True)
        if action == "apply":
            for name in ("candidate-target-file", "commission-receipt", "candidate-commission-receipt",
                         "bundle", "file-content-root", "effects"):
                command.add_argument("--" + name, type=Path, required=True)
            for name in ("candidate-id", "manifest-sha256", "effects-sha256"):
                command.add_argument("--" + name, required=True)
    args = parser.parse_args()
    try:
        print(execute(args))
        return 0
    except (RecoveryError, archive.ArchiveError, stage.StageError, OSError, ValueError, csv.Error, subprocess.SubprocessError) as error:
        print(str(error), file=sys.stderr)
        if args.action == "apply":
            print("No success is claimed. Use recover with the original tenant and operation if the COMMIT reply is unknown.", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
