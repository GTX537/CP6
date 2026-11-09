#!/usr/bin/env python3
"""Opt-in, real-process PostgreSQL verification for the RMA tenant migration profile."""

import argparse
import csv
import hashlib
import io
import json
import os
from pathlib import Path
import re
import secrets
import socket
import subprocess
import sys
import textwrap
import time
import uuid
from contextlib import contextmanager
from datetime import datetime, timezone

OWNER = "w350_owner"
A = "w350_a"
B = 'w350 Tenant B"ID'
CONTROL = "cp6_rma_tenant_v1.tenant_migrations"
HISTORY = "rma_migration_history_v1"
INSTALL = "install-rma-tenant-storage-v1"
MIGRATE = "migrate-rma-tenant-storage-v1"
WINDOWS = os.name == "nt"
FLAGS = subprocess.CREATE_NO_WINDOW if WINDOWS else 0
BASE_ENV = {key: os.environ[key] for key in os.environ if not key.upper().startswith("PG")}


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def token():
    return str(uuid.uuid4())


def q(value):
    return "'" + str(value).replace("'", "''") + "'"


def ident(value):
    return '"' + value.replace('"', '""') + '"'


def schema(tenant):
    return "t_" + uuid.UUID(tenant).hex


def wait_for(probe, message, seconds=7):
    until = time.monotonic() + seconds
    while time.monotonic() < until:
        value = probe()
        if value:
            return value
        time.sleep(0.07)
    raise RuntimeError(message)


class Fixture:
    def __init__(self, args, label):
        self.args = args
        self.root = args.root / label
        self.root.mkdir(mode=0o700)
        if WINDOWS:
            who = subprocess.check_output(["whoami", "/user", "/fo", "csv", "/nh"], creationflags=FLAGS)
            sid = next(csv.reader(io.StringIO(who.decode(errors="replace").strip())))[1]
            subprocess.run(["icacls", str(self.root), "/inheritance:r", "/grant:r", "*{}:(OI)(CI)F".format(sid)],
                           capture_output=True, check=True, creationflags=FLAGS)
        self.data = self.root / "data"
        self.log = self.root / "postgres.log"
        self.passwords = {role: secrets.token_hex(24) for role in (OWNER, A, B)}

    def scrub(self, data):
        for value in self.passwords.values():
            data = data.replace(value.encode("ascii"), b"[private fixture value]")
        return data

    def save(self, name, value):
        if not isinstance(value, bytes):
            value = (json.dumps(value, indent=2) + "\n").encode("utf-8")
        (self.root / name).write_bytes(self.scrub(value))

    def pg(self, tool):
        return str(self.args.pg_bin / (tool + (".exe" if WINDOWS else "")))

    def run_pg(self, label, tool, arguments, expected=0):
        # pg_ctl's server can inherit output handles on Windows. Files avoid waiting
        # for that server to close a pipe after pg_ctl itself has already exited.
        with (self.root / (label + ".stdout")).open("wb") as out, \
             (self.root / (label + ".stderr")).open("wb") as err:
            result = subprocess.run([self.pg(tool), *map(str, arguments)], cwd=self.root,
                                    env=BASE_ENV, stdout=out, stderr=err, timeout=60, creationflags=FLAGS)
        require(result.returncode == expected, "{} exit {}".format(label, result.returncode))

    @contextmanager
    def running(self):
        with socket.socket() as probe:
            probe.bind(("127.0.0.1", self.args.port))
        password_file = self.root / "initdb-password.private"
        try:
            password_file.write_text(self.passwords[OWNER] + "\n", encoding="ascii")
            self.run_pg("initdb", "initdb", ["-D", self.data, "-U", OWNER, "--pwfile", password_file,
                         "--auth-host=scram-sha-256", "--auth-local=scram-sha-256", "--no-locale", "-E", "UTF8"])
        finally:
            if password_file.exists():
                password_file.unlink()
        with (self.data / "postgresql.conf").open("a", encoding="ascii") as conf:
            conf.write("""
listen_addresses = '127.0.0.1'
port = {}
max_connections = 15
shared_buffers = '32MB'
log_statement = 'none'
log_min_error_statement = 'panic'
log_parameter_max_length = 0
log_parameter_max_length_on_error = 0
log_line_prefix = '%m [%p] %a xid=%x '
log_lock_waits = on
deadlock_timeout = '150ms'
""".format(self.args.port))
        pidfile = self.data / "postmaster.pid"
        try:
            self.run_pg("pg-start", "pg_ctl", ["-D", self.data, "-l", self.log, "-w", "-t", "20", "start"])
            for role in (A, B):
                self.sql("postgres", "CREATE ROLE {} LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB "
                         "NOCREATEROLE NOREPLICATION PASSWORD {};".format(ident(role), q(self.passwords[role])))
            self.sql("postgres", "ALTER SYSTEM SET log_statement TO 'all';")
            self.sql("postgres", "SELECT pg_reload_conf();")
            wait_for(lambda: self.sql("postgres", "SHOW log_statement;") == "all", "PG log reload")
            yield
        finally:
            if pidfile.exists():
                lines = pidfile.read_text(encoding="utf-8").splitlines()
                directory = self.sql("postgres", "SHOW data_directory;")
                require(Path(lines[1]).resolve() == self.data == Path(directory).resolve()
                        and int(lines[3]) == self.args.port, "private PG identity mismatch")
                self.run_pg("pg-stop", "pg_ctl", ["-D", self.data, "-m", "fast", "-w", "stop"])
                self.run_pg("pg-status", "pg_ctl", ["-D", self.data, "status"], expected=3)
                require(not pidfile.exists(), "private PostgreSQL pidfile remained")
                with socket.socket() as probe:
                    require(probe.connect_ex(("127.0.0.1", self.args.port)) != 0, "private PG listener remained")
                self.save("stopped.json", {"PostmasterPid": int(lines[0]), "Port": self.args.port})
            if self.log.exists():
                raw = self.log.read_bytes()
                scrubbed = self.scrub(raw)
                if raw != scrubbed:
                    self.log.write_bytes(scrubbed)
                    raise RuntimeError("PG logged a private fixture value")

    def psql(self, database, role=OWNER, app="W350-observer"):
        env = {**BASE_ENV, "PGPASSWORD": self.passwords[role], "PGAPPNAME": app,
               "PGCONNECT_TIMEOUT": "4", "PGSSLMODE": "disable"}
        return subprocess.Popen([self.pg("psql"), "-XqAtw", "-v", "ON_ERROR_STOP=1",
                                 "-v", "VERBOSITY=verbose", "-h", "127.0.0.1", "-p", str(self.args.port),
                                 "-U", role, "-d", database], cwd=self.root, env=env,
                                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                creationflags=FLAGS)

    def collect(self, proc, stdin=None, seconds=15):
        try:
            return proc.communicate(stdin, timeout=seconds)
        except subprocess.TimeoutExpired:
            proc.kill()
            out, err = proc.communicate()
            self.save("timeout-{}.stdout".format(proc.pid), out)
            self.save("timeout-{}.stderr".format(proc.pid), err)
            raise RuntimeError("process {} exceeded its deadline".format(proc.pid))

    def sql(self, database, statement, role=OWNER, error=None, label=None):
        with self.psql(database, role) as proc:
            out, err = self.collect(proc, (statement + "\n").encode("utf-8"), seconds=6)
            if label or proc.returncode:
                name = label or "sql-failure-{}".format(proc.pid)
                self.save(name + ".stdout", out)
                self.save(name + ".stderr", err)
            if error:
                require(proc.returncode != 0 and error.encode("ascii") in err, "expected SQLSTATE " + error)
            else:
                require(proc.returncode == 0, "psql exit {}".format(proc.returncode))
            return out.decode("utf-8").strip()

    def create_target(self, database, template=None):
        create = "CREATE DATABASE " + ident(database)
        if template:
            create += " TEMPLATE " + ident(template)
        self.sql("postgres", create + ";")
        return json.loads(self.sql(database, """
          SELECT json_build_object('Server',host(inet_server_addr()),'Port',inet_server_port(),
            'Database',d.datname,'DatabaseOid',d.oid::bigint,'SystemIdentifier',s.system_identifier::text)
          FROM pg_database d CROSS JOIN pg_control_system() s WHERE d.datname=current_database();
        """))

    def start_cli(self, command, request, role=OWNER):
        proc = subprocess.Popen([str(self.args.dotnet), str(self.args.cli), command, "--stdin"],
                                cwd=self.root, env=BASE_ENV, stdin=subprocess.PIPE,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=FLAGS)
        payload = {"Request": request, "AdminUsername": role, "AdminPassword": self.passwords[role],
                   "SslMode": "Disable"}
        proc.stdin.write(json.dumps(payload).encode("utf-8"))
        proc.stdin.close()
        proc.stdin = None
        return proc

    def receipt(self, label, proc, code=None, error=None):
        out, err = self.collect(proc)
        self.save(label + ".stdout", out)
        self.save(label + ".stderr", err)
        self.save(label + ".process.json", {"Pid": proc.pid, "ExitCode": proc.returncode,
                                           "StdoutBytes": len(out), "StderrBytes": len(err)})
        require(self.scrub(out + err) == out + err, "CLI exposed a private fixture value")
        require(code is None or proc.returncode == code,
                "{} expected CLI exit {}, observed {}".format(label, code, proc.returncode))
        if code == 0:
            require(out and not err, label + " did not return a clean receipt")
        if error:
            require(not out and error.encode("utf-8") in err, label + " missing rejection " + error)
        return out

    def invoke(self, label, command, request, code=0, error=None, role=OWNER):
        with self.start_cli(command, request, role) as proc:
            return self.receipt(label, proc, code, error)

    def activity(self, database):
        return json.loads(self.sql(database, """
          SELECT coalesce(json_agg(q),'[]'::json) FROM (
            SELECT a.pid,a.backend_xid::text AS xid,a.application_name,a.query,a.state,a.wait_event_type,a.wait_event,
              pg_blocking_pids(a.pid) AS blockers,
              (SELECT json_agg(l) FROM (SELECT locktype,mode,granted,relation::regclass::text,classid,objid,
                transactionid::text FROM pg_locks WHERE pid=a.pid) l) AS locks
            FROM pg_stat_activity a WHERE a.datname=current_database() AND a.pid<>pg_backend_pid()
          ) q;
        """))

    def state(self, database, tenant):
        tenant_schema = schema(tenant)
        row = json.loads(self.sql(database, """
            SELECT json_build_object('control',row_to_json(m),
              'schema_oid',to_regnamespace({s})::oid::bigint,
              'heads_oid',to_regclass({heads})::oid::bigint,
              'versions_oid',to_regclass({versions})::oid::bigint,
              'history_oid',to_regclass({history})::oid::bigint)
            FROM {control} m WHERE tenant_id={tenant};
        """.format(s=q(tenant_schema), heads=q(tenant_schema + ".heads"), versions=q(tenant_schema + ".versions"),
                   history=q(tenant_schema + "." + HISTORY), control=CONTROL, tenant=q(tenant))))
        row["local"] = (json.loads(self.sql(database, "SELECT row_to_json(h) FROM {}.{} h;".format(
            tenant_schema, HISTORY))) if row["history_oid"] else None)
        return row


def install_request(target, tenant_a, tenant_b):
    return {"Target": target, "InstallationId": token(),
            "Tenants": [{"TenantId": tenant_a, "Login": A}, {"TenantId": tenant_b, "Login": B}]}


def migration_request(install, tenant, operation):
    return {"Target": install["Target"], "InstallationId": install["InstallationId"],
            "TenantId": tenant, "OperationId": operation}


def ddl_rma_fixture(f, database, role, tenant, application, actor, outbound):
    name = schema(tenant)
    f.sql(database, """
        BEGIN;
        INSERT INTO {s}.heads (tenant_id,id,actor_id,outbound_id,version)
          VALUES ({tenant},{application},{actor},{outbound},1);
        INSERT INTO {s}.versions (tenant_id,id,version,state,payload)
          VALUES ({tenant},{application},1,'DRAFT','{{"verified":true}}'::jsonb);
        INSERT INTO {s}.versions (tenant_id,id,version,state,payload)
          VALUES ({tenant},{application},2,'SUBMITTED','{{"verified":true}}'::jsonb);
        UPDATE {s}.heads SET version=2 WHERE tenant_id={tenant} AND id={application};
        COMMIT;
    """.format(s=name, tenant=q(tenant), application=q(application), actor=q(actor), outbound=q(outbound)), role=role)
    return read_rma(f, database, role, tenant)


def read_rma(f, database, role, tenant):
    return f.sql(database, """
        SELECT session_user||'|'||cp6_tenancy_v1.current_tenant_id()||'|'||h.id||'|'||v.state
          FROM {s}.heads h JOIN {s}.versions v USING (tenant_id,id,version);
    """.format(s=schema(tenant)), role=role)


def verify_ready(state, receipt):
    result = json.loads(receipt)
    row = state["control"]
    local = state["local"]
    require(state["schema_oid"] and state["heads_oid"] and state["versions_oid"] and state["history_oid"]
            and row["state"] == "Ready" and local["tenant_id"] == result["TenantId"]
            and row["operation_id"] == local["operation_id"] == result["OperationId"]
            and row["script_sha256"] == local["script_sha256"] == result["ScriptSha256"]
            and row["migration_id"] == local["migration_id"] == result["MigrationId"] and row["lineage"] == local["lineage"] == result["Lineage"]
            and row["result_json"] == local["result_json"] == receipt.decode("utf-8").strip(),
            "DDL, history, Ready and receipt differ")


def fresh(f):
    database = "rma_fresh"
    tenant_a, tenant_b, app = token(), token(), token()
    target = f.create_target(database)
    install = install_request(target, tenant_a, tenant_b)
    operation = token()
    request_a, request_b = migration_request(install, tenant_a, operation), migration_request(install, tenant_b, operation)
    f.save("requests.json", {"Install": install, "A": request_a, "B": request_b})
    setup = f.invoke("01-bootstrap", INSTALL, install)
    f.invoke("01-wrong-target", MIGRATE, {**request_a, "Target": {**target, "DatabaseOid": target["DatabaseOid"] + 1}},
             1, "target-identity-rejected")
    f.invoke("01-ordinary-control-denied", MIGRATE, request_a, 1, "target-identity-rejected", role=A)
    f.invoke("01-unknown-tenant", MIGRATE, {**request_a, "TenantId": token()}, 1, "binding-rejected")
    cloned = f.create_target("rma_cloned", template=database)
    f.invoke("01-cloned-target", MIGRATE, {**request_a, "Target": cloned}, 1, "binding-rejected")
    require(f.state("rma_cloned", tenant_a)["schema_oid"] is None, "cloned profile acquired a business authority")
    a_before = f.state(database, tenant_a)
    b_before = f.state(database, tenant_b)
    f.save("01-before.json", {"A": a_before, "B": b_before})
    require(all(row["schema_oid"] is None and row["control"]["state"] == "Provisioning"
                for row in (a_before, b_before)), "bootstrap opened a business schema")

    # Test-only A-specific gate on the final shared write, after local DDL and history.
    f.sql(database, """
      CREATE FUNCTION cp6_rma_tenant_v1.w350_wait() RETURNS trigger LANGUAGE plpgsql AS $$
      BEGIN PERFORM pg_advisory_xact_lock(14629701,1); RETURN NEW; END $$;
      CREATE TRIGGER w350_wait BEFORE UPDATE ON {}
        FOR EACH ROW WHEN (OLD.tenant_id={}::uuid AND NEW.state='Ready')
        EXECUTE FUNCTION cp6_rma_tenant_v1.w350_wait();
    """.format(CONTROL, q(tenant_a)))
    holder = f.psql(database, app="W350-holder")
    clients = []
    try:
        holder.stdin.write(b"BEGIN; SELECT pg_advisory_xact_lock(14629701,1);\n")
        holder.stdin.flush()
        hold = wait_for(lambda: next((r for r in f.activity(database) if r["application_name"] == "W350-holder"
                                      and r["state"] == "idle in transaction"), None), "holder did not lock")
        a1 = f.start_cli(MIGRATE, request_a)
        clients.append(a1)
        first = wait_for(lambda: next((r for r in f.activity(database)
                                       if r["blockers"] == [hold["pid"]] and r["wait_event"] == "advisory"
                                       and "UPDATE " + CONTROL in r["query"]), None), "A did not reach Ready write")
        a2 = f.start_cli(MIGRATE, request_a)
        clients.append(a2)
        second = wait_for(lambda: next((r for r in f.activity(database)
                                        if first["pid"] in r["blockers"] and "FOR UPDATE" in r["query"]), None),
                          "second A did not wait on the control row")
        f.save("02-lock-chain.json", {"Holder": hold, "A1": first, "A2": second,
                                      "A1Process": a1.pid, "A2Process": a2.pid})
        log = f.log.read_text(encoding="utf-8", errors="replace").splitlines()
        a_log = [line for line in log if "[{}]".format(first["pid"]) in line and "xid={} ".format(first["xid"]) in line]
        f.save("02-a-transaction.log", ("\n".join(a_log) + "\n").encode("utf-8"))
        require(any('CREATE SCHEMA "' + schema(tenant_a) + '"' in line for line in a_log)
                and any('INSERT INTO "' + schema(tenant_a) + '".' + HISTORY in line for line in a_log),
                "A's DDL and local history did not precede the shared Ready wait")
        b_result = f.invoke("03-b-independent", MIGRATE, request_b)
        b_state = f.state(database, tenant_b)
        verify_ready(b_state, b_result)
        b_actor, b_outbound = token(), token()
        b_read = ddl_rma_fixture(f, database, B, tenant_b, app, b_actor, b_outbound)
        f.save("03-b-while-a-blocked.json", {"State": b_state, "OrdinaryRead": b_read,
                                             "StillBlocked": f.activity(database)})
        require(b_read == "{}|{}|{}|SUBMITTED".format(B, tenant_b, app)
                and a1.poll() is None and a2.poll() is None
                and any(r["pid"] == first["pid"] and r["blockers"] == [hold["pid"]]
                        for r in f.activity(database)), "B did not commit and read while A waited")

        require(f.sql(database, "SELECT pg_cancel_backend({});".format(second["pid"])) == "t", "cancel A2")
        f.receipt("04-a2-cancelled", a2, 1, "57014")
        a1.kill()
        output = f.receipt("04-a1-killed", a1)
        require(a1.returncode and not output, "terminated A unexpectedly emitted a receipt")
    finally:
        for proc in clients:
            if proc.poll() is None:
                proc.kill()
                f.receipt("cleanup-{}".format(proc.pid), proc)
        out, err = f.collect(holder, b"ROLLBACK;\n")
        f.save("04-holder.stdout", out)
        f.save("04-holder.stderr", err)
    require(holder.returncode == 0 and not err, "holder rollback failed")
    wait_for(lambda: not any(row["pid"] in (first["pid"], second["pid"]) for row in f.activity(database)),
             "interrupted A backend remained")
    after_a, after_b = f.state(database, tenant_a), f.state(database, tenant_b)
    after_b_read = read_rma(f, database, B, tenant_b)
    require(after_a == a_before and after_b == b_state and after_b_read == b_read,
            "interrupted A left half-state or changed B")
    f.save("04-after-interrupt.json", {"A": after_a, "B": after_b, "OrdinaryB": after_b_read})
    f.sql(database, "DROP TRIGGER w350_wait ON {}; DROP FUNCTION cp6_rma_tenant_v1.w350_wait();".format(CONTROL))

    a_result = f.invoke("05-a-retry", MIGRATE, request_a)
    a_state = f.state(database, tenant_a)
    verify_ready(a_state, a_result)
    require(f.invoke("05-a-replay", MIGRATE, request_a) == a_result
            and f.invoke("05-b-replay", MIGRATE, request_b) == b_result
            and f.invoke("05-bootstrap-replay", INSTALL, install) == setup,
            "same-key replay did not return original bytes")
    f.invoke("05-ready-conflict", MIGRATE, {**request_a, "OperationId": token()}, 1, "operation-conflict")
    history = schema(tenant_a) + "." + HISTORY
    f.sql(database, "UPDATE {} SET script_sha256={};".format(history, q("0" * 64)))
    f.invoke("05-local-history-drift", MIGRATE, request_a, 1, "migration-history-mismatch")
    f.sql(database, "UPDATE {} SET script_sha256={};".format(history, q(json.loads(a_result)["ScriptSha256"])))
    a_read = ddl_rma_fixture(f, database, A, tenant_a, app, token(), token())
    require(a_read == "{}|{}|{}|SUBMITTED".format(A, tenant_a, app), "same application UUID in A failed")
    a_schema, b_schema = schema(tenant_a), schema(tenant_b)
    f.sql(database, "SET app.tenant_id={}; SELECT * FROM {}.heads;".format(q(tenant_b), b_schema), A, "42501", "06-cross-schema")
    f.sql(database, "SELECT * FROM {}.{};".format(a_schema, HISTORY), A, "42501", "06-private-history")
    f.sql(database, "SELECT * FROM {};".format(CONTROL), A, "42501", "06-private-control")
    f.sql(database, "CREATE TEMP TABLE forbidden (id int);", A, "42501", "06-no-temp")
    f.sql(database, "CREATE TABLE {}.forbidden (id int);".format(a_schema), A, "42501", "06-no-ddl")
    f.sql(database, "UPDATE {}.heads SET outbound_id={};".format(a_schema, q(token())), A, "42501", "06-no-head-rewrite")
    f.sql(database, "DELETE FROM {}.versions;".format(a_schema), A, "42501", "06-no-version-delete")
    f.sql(database, "INSERT INTO {}.heads VALUES ({},{},{},{},1);".format(
        a_schema, q(tenant_b), q(token()), q(token()), q(token())), A, "23514", "06-fixed-tenant")
    f.sql(database, "BEGIN; INSERT INTO {}.heads VALUES ({},{},{},{},1); COMMIT;".format(
        a_schema, q(tenant_a), q(token()), q(token()), q(token())), A, "23503", "06-deferred-fk")
    f.sql(database, "UPDATE {}.versions SET state='SUBMITTED';".format(a_schema),
          error="23514", label="06-native-immutable-update")
    f.sql(database, "TRUNCATE {0}.heads, {0}.versions;".format(a_schema), error="23514", label="06-native-immutable-truncate")
    require(f.state(database, tenant_a) == a_state and f.state(database, tenant_b) == b_state,
            "unauthorized operations or replay changed migration state")
    f.save("07-final.json", {"A": a_state, "B": b_state, "OrdinaryA": a_read, "OrdinaryB": b_read,
                             "ReceiptSha256A": hashlib.sha256(a_result).hexdigest(),
                             "ReceiptSha256B": hashlib.sha256(b_result).hexdigest()})


def legacy(f):
    database = "rma_existing_shared"
    target = f.create_target(database)
    migration = Path(__file__).resolve().parents[3] / "CP6.Persistence.PostgreSql/Migrations/Core/20261030022000_RmaApplicationV1.cs"
    source = migration.read_text(encoding="utf-8-sig")
    sql = textwrap.dedent(re.search(r'migrationBuilder\.Sql\("""(.*?)"""\);', source, re.S).group(1))
    f.sql(database, sql)
    f.save("legacy-rma-source.json", {"Sha256": hashlib.sha256(migration.read_bytes()).hexdigest()})
    f.invoke("01-shared-rejected", INSTALL, install_request(target, token(), token()), 1, "fresh-layout-required")

    database = "rma_existing_combined"
    target = f.create_target(database)
    tenant = token()
    prior = {"OperationId": token(), "TenantId": tenant, "RuntimeLogin": A, "Host": target["Server"],
             "Port": target["Port"], "Database": target["Database"], "DatabaseOid": target["DatabaseOid"],
             "SystemIdentifier": target["SystemIdentifier"], "Profile": "erp-identity-messaging-v1"}
    original = f.invoke("02-existing-combined", "provision", prior)
    f.invoke("03-combined-rejected", INSTALL, install_request(target, tenant, token()), 1, "fresh-layout-required")
    require(f.invoke("04-existing-combined-replay", "provision", prior) == original,
            "fresh-only rejection changed the original combined receipt")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True, help="Existing owned evidence directory")
    parser.add_argument("--pg-bin", type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--cli", type=Path, required=True, help="Built CP6.TenantStorage.dll")
    args = parser.parse_args()
    args.root, args.pg_bin, args.dotnet, args.cli = (
        path.resolve(strict=True) for path in (args.root, args.pg_bin, args.dotnet, args.cli))
    args.root /= "rma-migration-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + uuid.uuid4().hex[:8]
    args.root.mkdir(mode=0o700)
    print(str(args.root), flush=True)
    for name, scenario in (("fresh", fresh), ("legacy", legacy)):
        fixture = Fixture(args, name)
        fixture.save("binary.json", {"CliSha256": hashlib.sha256(args.cli.read_bytes()).hexdigest()})
        with fixture.running():
            scenario(fixture)
        print(name + " passed", flush=True)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except RuntimeError as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
