"""Native recovery assertions for an explicitly owned, disposable PostgreSQL fixture.

The fixture supplies established source SQL callbacks and the real live_recovery
arguments after creating a complete v12 archive and all five closed candidate
receipts. It also owns credentials, process shutdown and the HTTP assertions.
This helper deliberately simulates row loss; it is not an operator recovery CLI.
"""
import json
from types import SimpleNamespace
from unittest.mock import patch
from uuid import uuid4

import archive
import live_recovery as live


def verify(args, read, sql, foreign_tenant):
    tenant = archive.guid(args.tenant)
    schema = "t_" + tenant.hex
    foreign_schema = "t_" + archive.guid(foreign_tenant).hex
    output = args.output

    def require(condition, message):
        if not condition:
            raise RuntimeError(message)

    def snapshot(label):
        relations = [schema + ".heads", schema + ".versions", foreign_schema + ".heads",
                     foreign_schema + ".versions", archive.JOURNAL_TABLE,
                     schema + "." + archive.HISTORY, foreign_schema + "." + archive.HISTORY,
                     "cp6_rma_model_v2.tenant_migrations", 'public."Sys_Users"', 'public."Sys_RoleAction"']
        values = [archive.literal(relation) + ", (SELECT encode(sha256(convert_to(" +
                  "coalesce(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text),'[]')::text,'UTF8')),'hex') FROM " +
                  relation + " t)" for relation in relations]
        return read(label, "SELECT json_build_object(" + ",".join(values) + ");")

    def call(label, action="apply"):
        return live.execute(SimpleNamespace(**{**vars(args), "output": output / label,
                                               "operation": operation, "action": action}))

    before = snapshot("v2-native-before")
    require(read("v2-native-nonempty", f"SELECT to_json(count(*)) FROM {schema}.heads;") > 0,
            "nonempty simulated source required")
    sql("v2-native-simulate-loss", f"""BEGIN;
        ALTER TABLE {schema}.versions DISABLE TRIGGER rma_application_immutable;
        DELETE FROM {schema}.versions; DELETE FROM {schema}.heads;
        SET CONSTRAINTS ALL IMMEDIATE;
        ALTER TABLE {schema}.versions ENABLE TRIGGER rma_application_immutable;
        COMMIT;""")
    damaged = snapshot("v2-native-damaged")
    operation = str(uuid4())
    reached = []

    def fault(query, current_tenant):
        require(query(f"SELECT to_json(count(*)) FROM {schema}.heads;") > 0,
                "fault must follow actual restoration")
        reached.append(True)
        query("DO $fault$ BEGIN RAISE EXCEPTION 'v2-native-late-recovery-fault'; END $fault$; SELECT to_json(true);")

    with patch.object(live.storage_model_v2, "end_recovery", side_effect=fault):
        try:
            call("late-fault")
        except archive.ArchiveError:
            require(reached == [True], "failure occurred before the intended native fault")
        else:
            raise RuntimeError("late recovery fault committed")
    require(snapshot("v2-native-rollback") == damaged, "late fault changed business/security originals")
    require(read("v2-native-no-failed-receipt", f"SELECT to_json(count(*)) FROM {live.RECEIPTS} "
                 f"WHERE tenant_id={archive.literal(tenant)} AND operation_id={archive.literal(operation)};") == 0,
            "failed recovery retained a receipt")
    require(read("v2-native-no-failed-permit", "SELECT to_json(count(*)) FROM cp6_rma_model_v2.writer_transactions;") == 0,
            "failed recovery retained a writer permit")
    original = call("restore")
    require(original == call("same-key") == call("recover", "recover"), "same-key receipt changed")
    require(snapshot("v2-native-restored") == before, "restoration changed typed rows or current security state")
    require(read("v2-native-no-permit", "SELECT to_json(count(*)) FROM cp6_rma_model_v2.writer_transactions;") == 0,
            "completed recovery retained a writer permit")
    result = json.loads(original)
    require(result["profile"] == live.PROFILE_STORAGE_V2 and result["recoveryReady"]
            and result["automaticSends"] == 0, "wrong recovery result")
    return result
