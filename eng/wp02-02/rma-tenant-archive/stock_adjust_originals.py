#!/usr/bin/env python3
"""Export a full v11 cut with the native rows of one original Stock adjustment."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
from typing import NamedTuple

sys.dont_write_bytecode = True
import archive
import file_content_assets as assets
import file_content_history as paths


FORMAT = "cp6-stock-adjust-originals-v1"
Q = archive.literal
CHAR = "bpchar"
TIME = "timestamp without time zone"
QTY = "numeric(21,8)"
PRICE = "numeric(18,4)"
UUID = "uuid"
BOOL = "boolean"
TOKEN = "bytea"


class Effect(NamedTuple):
    member: str
    relation: str
    columns: tuple
    types: tuple


def effect(member, relation, columns, types):
    return Effect(member, relation, tuple(columns.split()), types)


EFFECTS = (
    effect("stock.csv", 'public."T_Stock"',
           """Id WarehouseCd LocationCd ProductCd LotNo PhysicalQty AllocatedQty AvailableQty UnitCd
              ReceiveDate ExpiryDate UnitPrice RecallFlag OwnerType OwnerCd PaperRollNo QcStatus
              Creator CreateDate Modifier ModifyDate TenantId IsDeleted RowVersion""",
           (UUID, *[CHAR] * 4, QTY, QTY, QTY, CHAR, TIME, TIME, PRICE, BOOL, *[CHAR] * 5,
            TIME, CHAR, TIME, UUID, BOOL, TOKEN)),
    effect("stock-transaction.csv", 'public."T_StockTransaction"',
           """Id TxnNo TxnType TxnDateTime WarehouseCd LocationCd ProductCd LotNo Qty UnitCd UnitPrice
              RelatedNo RelatedType CounterLocationCd OperatorCd Remark ReceiptInspectionNo KitOrderNo RmaNo
              CrossDockFlag OwnerType OwnerCd PaperRollNo ConsumedLengthM Creator CreateDate Modifier
              ModifyDate TenantId IsDeleted RowVersion""",
           (UUID, CHAR, CHAR, TIME, *[CHAR] * 4, QTY, CHAR, PRICE, *[CHAR] * 8,
            BOOL, CHAR, CHAR, CHAR, QTY, CHAR, TIME, CHAR, TIME, UUID, BOOL, TOKEN)),
    effect("wms-sequence.csv", 'public."T_WmsSequence"',
           "Id Prefix DateKey NextNo Creator CreateDate Modifier ModifyDate TenantId IsDeleted RowVersion",
           (UUID, CHAR, CHAR, "integer", CHAR, TIME, CHAR, TIME, UUID, BOOL, TOKEN)),
    effect("movement-sequence.csv", "cp6_stock_return_v1.movement_sequences",
           "sequence tenant_id pool_id movement_id", ("bigint", UUID, UUID, UUID)),
)


def read_effect_keys(pg, connection, logs, snapshot, tenant, operation, stock):
    tenant_sql, operation_sql = Q(tenant), Q(operation)
    sql = f"""
    BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
    SET TRANSACTION SNAPSHOT {Q(snapshot)};
    SET LOCAL search_path=pg_catalog;
    SET LOCAL row_security=off;
    SET LOCAL statement_timeout='30s';
    WITH original AS (
        SELECT convert_from(input_bytes,'UTF8')::jsonb AS input,
               convert_from(result_bytes,'UTF8')::jsonb AS result,audit_json,object_ids
        FROM {stock.relation(stock.JOURNAL)}
        WHERE tenant_id={tenant_sql} AND owner={Q(stock.JOURNAL_OWNER)} AND operation_id={operation_sql}
          AND state='COMMITTED' AND contract={Q(stock.JOURNAL_CONTRACTS[0])}
          AND input_sha256=encode(sha256(input_bytes),'hex')
    ), effect AS (
        SELECT (result->'StockIds'->>0)::uuid AS pool,(result->'TransactionIds'->>0)::uuid AS movement
        FROM original
        WHERE input @> jsonb_build_object('kind','APPLY','request',jsonb_build_object(
                  'OperationId',{operation_sql}::uuid,'TxnType','ADJ','RelatedNo',NULL,'RelatedType',NULL))
          AND result @> jsonb_build_object('Result',jsonb_build_object(
                  'OperationId',{operation_sql}::uuid,'Kind','APPLY','OutTxnNo',NULL,'InTxnNo',NULL))
          AND jsonb_array_length(result->'StockIds')=1 AND jsonb_array_length(result->'TransactionIds')=1
          AND object_ids=ARRAY[(result->'StockIds'->>0)::uuid]
          AND audit_json->>'action'='adjust'
          AND jsonb_array_length(audit_json->'before')=1 AND jsonb_array_length(audit_json->'after')=1
          AND jsonb_array_length(audit_json->'transactions')=1
    )
    SELECT json_build_object('stockId',e.pool,'transactionId',e.movement)
    FROM effect e
    WHERE (SELECT count(*) FROM {EFFECTS[3].relation} WHERE tenant_id={tenant_sql} AND pool_id=e.pool)=1
      AND NOT EXISTS (SELECT FROM cp6_operations_owner_v1.department_mappings WHERE tenant_id={tenant_sql}
           AND ((object_kind='stock' AND object_id=e.pool) OR (object_kind='stock-transaction' AND object_id=e.movement)))
      AND NOT EXISTS (SELECT FROM public."Sys_SecurityLogs" WHERE "TenantId"={tenant_sql}
           AND "ContextJson"->>'kind'='stock-transaction-inheritance.v1' AND "ContextJson"->>'transactionId'=e.movement::text);
    COMMIT;
    """
    archive.run_native(pg, "psql", ["-XqAtw", "-v", "ON_ERROR_STOP=1", *connection], os.environ, logs,
                       "stock-adjust-effect-keys", sql=sql)
    result = (logs / "stock-adjust-effect-keys.stdout").read_bytes()
    if not result.strip():
        raise archive.ArchiveError("stock-adjust-original-scope-rejected")
    keys = json.loads(result)
    return archive.guid(keys["stockId"]), archive.guid(keys["transactionId"])


def export(args):
    pg = args.pg_bin.resolve(strict=True)
    target = archive.PhysicalTarget.from_mapping(json.loads(args.target_file.read_text(encoding="utf-8-sig")))
    tenant, operation = archive.guid(args.tenant), archive.guid(args.operation)
    root = assets.checked_root(args.file_content_root.absolute()).resolve(strict=True)
    output = paths.checked_output(args.output.absolute(), root)
    paths.checked_output(output, args.target_file.resolve(strict=True))
    effects, logs = output / "stock-effects", output / "native"
    stock = archive.stock_outbox_module()

    def same_snapshot(snapshot):
        effects.mkdir(mode=0o700)
        connection = archive.connection_args(target, args.username)
        stock_id, movement = read_effect_keys(pg, connection, logs, snapshot, tenant, operation, stock)
        predicates = (f'"TenantId"={Q(tenant)} AND "Id"={Q(stock_id)}',
                      f'"TenantId"={Q(tenant)} AND "Id"={Q(movement)}',
                      f'''"TenantId"={Q(tenant)} AND "Prefix"='TXN' AND "DateKey"='*' AND NOT "IsDeleted"''',
                      f"tenant_id={Q(tenant)} AND pool_id={Q(stock_id)} AND movement_id={Q(movement)}")
        for part, predicate in zip(EFFECTS, predicates, strict=True):
            fields = ",".join(map(stock.ident, part.columns))
            destination = effects / part.member
            archive.copy_snapshot(pg, connection, os.environ, logs, destination, part.member.removesuffix(".csv"),
                                  snapshot, f"SELECT {fields} FROM {part.relation} WHERE {predicate}",
                                  archive.JOURNAL_COPY_SETTINGS)

    bundle = archive.export_bundle(pg, target, args.username, args.installation, args.tenant, output,
        include_original_journal=True, include_rma_source=True, include_rma_visibility=True,
        include_identity_priority=True, include_rma_iam_references=True, file_content_root=root,
        include_erp_integration=True, include_privacy_evidence=True, include_main_stock_outbox=True,
        include_file_content_history=True, on_snapshot=same_snapshot)
    manifest_file = bundle / "manifest.json"
    manifest = {
        "format": FORMAT, "tenantId": str(tenant), "operationId": str(operation),
        "sourceManifestSha256": archive.file_digest(manifest_file)["sha256"],
        "members": {part.member: archive.file_digest(effects / part.member) for part in EFFECTS},
    }
    with (effects / "manifest.json").open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(manifest, stream, indent=2)
        stream.write("\n")
    return effects


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for field in ("pg-bin", "target-file", "file-content-root", "output"):
        parser.add_argument("--" + field, type=Path, required=True)
    for field in ("username", "installation", "tenant", "operation"):
        parser.add_argument("--" + field, required=True)
    try:
        print(export(parser.parse_args()))
    except (archive.ArchiveError, OSError, ValueError, subprocess.SubprocessError) as error:
        print("Stock adjustment originals rejected:", error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
