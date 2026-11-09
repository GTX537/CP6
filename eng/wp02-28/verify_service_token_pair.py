"""Exercise the two W345 published hosts against one isolated service-token database."""

import argparse
import base64
from contextlib import contextmanager
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import re
import secrets
import socket
import ssl
import subprocess
import time
from urllib.error import HTTPError
from urllib.parse import urlencode
from urllib.request import HTTPSHandler, ProxyHandler, Request, build_opener
from uuid import uuid4

import pg_resource as pg
import published_artifact as artifact
from verify_host import DOTNET, digest, process_identity, write
from verify_published_host import isolated_environment


RELEASES = {
    "prior": ("585ab63045930fcae131305c927aadb458fa544e",
              "2d46471a3620ad50aec4c467503a38fc84724f759f209658e3d87cb0fdb460bc"),
    "current": ("634ca5cc2ffda8eadbfd109c922ce59dd2d3eb15",
                "036e23de34c230c484737ecf12e97b81437118d2587b8ea05e42500f714f7bf7"),
}
DRIVERS = {"Npgsql": "8.0.8", "Npgsql.EntityFrameworkCore.PostgreSQL": "8.0.11"}
ISSUER = "https://127.0.0.1"
TOKEN_EVENT = "com.gtx537.platform.token.revoked.v1"
DATABASE = "w347_service_token"


def verified_releases(args):
    releases = {}
    for name, (source, pin) in RELEASES.items():
        directory = args.publish_root / name
        receipt = artifact.verify(directory, args.publish_root / f"{name}.receipt.json",
                                  pin, source, args.package_cache)
        deps = json.loads((directory / artifact.DEPS).read_bytes())
        runtime = deps["targets"][deps["runtimeTarget"]["name"]]
        for package, version in DRIVERS.items():
            pg.require(f"{package}/{version}" in runtime and package + ".dll" in receipt["requiredFiles"],
                       f"{name}: unexpected driver dependency")
        releases[name] = {
            "directory": str(directory), "sourceSha": source, "receiptSha256": pin,
            "entrySha256": receipt["files"][artifact.ENTRY],
            "driverFiles": {package + ".dll": receipt["files"][package + ".dll"] for package in DRIVERS},
        }
    return releases


def cli(storage, command, request, control, log):
    payload = {"Request": request, "AdminUsername": control["Admin"],
               "AdminPassword": control["AdminPassword"], "SslMode": "Disable"}
    result = subprocess.run([str(DOTNET), str(storage), command, "--stdin"],
                            input=json.dumps(payload), text=True, encoding="utf-8",
                            capture_output=True, timeout=180, env=isolated_environment(),
                            creationflags=pg.NO_WINDOW)
    log.write_text(result.stdout + result.stderr, encoding="utf-8")
    pg.require(result.returncode == 0, f"{command} failed ({result.returncode}); see private log")
    return json.loads(result.stdout)


def install(control, storage, root, report):
    tenants = [{"TenantId": str(uuid4()), "Login": "w347_tenant_" + tag,
                "Password": secrets.token_hex(24), "CredentialVersion": "1"} for tag in ("a", "b")]
    for tenant in tenants:
        pg.sql(control, f"""CREATE ROLE "{tenant['Login']}" LOGIN NOSUPERUSER NOBYPASSRLS
                  NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT PASSWORD '{tenant['Password']}';""", "postgres")
    pg.sql(control, f'CREATE DATABASE "{DATABASE}" OWNER "{control["Admin"]}" TEMPLATE template0;', "postgres")
    identity = pg.identity(control, DATABASE)
    version = pg.sql(control, "SHOW server_version;", DATABASE)
    pg.require(version.split()[0] == "18.6", f"Expected PostgreSQL 18.6, got {version}")
    request = {
        "InstallationId": str(uuid4()), "Server": "127.0.0.1", "Port": pg.PORT,
        "Database": DATABASE, "DatabaseOid": int(identity["Oid"]), "SystemIdentifier": identity["SystemIdentifier"],
        "Tenants": [{key: tenant[key] for key in ("TenantId", "Login")} for tenant in tenants],
    }
    report["database"] = identity | {"serverVersion": version}
    receipt = cli(storage, "install-service-token-v1", request, control, root / "install.log")
    pg.require(receipt["InstallationId"] == request["InstallationId"] and
               receipt["Profile"] == "service-token-v1" and receipt["DatabaseOid"] == request["DatabaseOid"]
               and receipt["SystemIdentifier"] == request["SystemIdentifier"], "Installer receipt identity differs")
    report["installation"] = receipt
    for tag, tenant in zip(("a", "b"), tenants):
        result = cli(storage, "service-token-tenant-directory-control-v1", {
            "Installation": request, "TenantId": tenant["TenantId"], "Operation": "initialize",
            "TenantCode": "w347-" + tag, "TenantName": "Synthetic tenant " + tag,
            "Desired": {"Enable": True, "ExpireDate": None},
        }, control, root / f"tenant-{tag}.log")
        pg.require(result["Outcome"] == "INITIALIZED", f"Tenant {tag} was not initialized")
    return request, tenants


def catalog(control):
    return json.loads(pg.sql(control, f"""
        SELECT json_build_object(
          'profile', i.profile, 'installationId', i.installation_id, 'baselineSha256', i.baseline_sha256,
          'fingerprint', i.fingerprint, 'liveFingerprint', cp6_service_token_v1.fingerprint(),
          'efHistoryTables', (SELECT count(*) FROM pg_class WHERE relname LIKE '__EFMigrationsHistory%'),
          'tokenRlsForced', (SELECT relrowsecurity AND relforcerowsecurity FROM pg_class
                             WHERE oid='crm_identity."ServiceToken"'::regclass),
          'defaultMayWriteToken', has_table_privilege('{control["Runtime"]}', 'crm_identity."ServiceToken"', 'INSERT,UPDATE'))
        FROM cp6_service_token_v1.installation i WHERE singleton;
        """, DATABASE))


def observe(control):
    return json.loads(pg.sql(control, """
        SELECT json_build_object(
          'tokens', coalesce((SELECT json_agg(t ORDER BY "TenantId","Jti") FROM crm_identity."ServiceToken" t),'[]'),
          'snapshots', coalesce((SELECT json_agg(s ORDER BY "TenantId","AggregateId") FROM crm_identity."Snapshot" s),'[]'),
          'outbox', coalesce((SELECT json_agg(o ORDER BY "Id") FROM crm_identity_priority."Cp6_OutboxMessage" o),'[]'),
          'generations', coalesce((SELECT json_object_agg("TenantId","Generation")
                                   FROM public."CrmIdentityTenantGenerations"),'{}'));
        """, DATABASE))


def issue_certificate(root):
    password = secrets.token_hex(24)
    private = json.loads(pg.run(["pwsh", "-NoProfile", "-NonInteractive", "-Command", """
        $password = [Console]::ReadLine()
        $tls = [System.Security.Cryptography.RSA]::Create(2048)
        $signing = [System.Security.Cryptography.RSA]::Create(2048)
        $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=127.0.0.1', $tls, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $san = [System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
        $san.AddIpAddress([System.Net.IPAddress]::Loopback)
        $request.CertificateExtensions.Add($san.Build())
        $cert = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddHours(2))
        try {
            @{pfx=[Convert]::ToBase64String($cert.Export('Pfx', $password))
              certificate=$cert.ExportCertificatePem(); signing=$signing.ExportRSAPrivateKeyPem()} |
              ConvertTo-Json -Compress
        } finally { $cert.Dispose(); $tls.Dispose(); $signing.Dispose() }
        """], data=password + "\n"))
    pfx = root / "loopback.pfx"
    pfx.write_bytes(base64.b64decode(private["pfx"]))
    context = ssl.create_default_context(cadata=private["certificate"])
    return private["signing"], {"Path": str(pfx), "Password": password}, context


def configuration(control, request, tenants, clients, signing, certificate, root):
    now = datetime.now(timezone.utc)
    organizations = [{"TenantId": tenant["TenantId"], "Slug": "w347-" + tag, "Region": "us", "CrmEnabled": True}
                     for tag, tenant in zip(("a", "b"), tenants)]
    return {
        "Startup": {"SkipDatabaseInitialization": True, "SkipHostedServices": True},
        "Database": {"Provider": "PostgreSql"},
        "ConnectionStrings": {"DefaultConnection": f'Host=127.0.0.1;Port={pg.PORT};Database={DATABASE};'
            f'Username={control["Runtime"]};Password={control["RuntimePassword"]};SSL Mode=Disable;Timeout=3',
            "Redis": ""},
        "JWT": {"Issuer": "CP6", "Audience": "CP6.Web", "Secret": secrets.token_hex(32)},
        "CrmOidc": {"Enabled": True, "Issuer": ISSUER, "ActiveKeyId": "w347",
            "Keys": [{"Kid": "w347", "Pem": signing}],
            "Clients": [{"ClientId": "CP6.Web", "SecretSha256": digest(secrets.token_bytes(32)),
                         "RedirectUris": [ISSUER + "/callback"]}],
            "Organizations": organizations,
            "ServiceClients": [{"ClientId": client["id"], "TenantId": client["tenant"], "Enabled": True,
                "SecretSha256": digest(client["secret"].encode()), "AllowedScopes": ["cp6.services"]}
                for client in clients]},
        "CrmIdentity": {"Enabled": True, "Tenants": {entry["TenantId"]: entry["Region"] for entry in organizations},
            "ProjectionReaderClientIds": [clients[1]["id"]],
            "ServiceTokenTenancy": {**request, "Enabled": True, "Revision": "1", "SslMode": "Disable",
                "ValidFromUtc": (now - timedelta(minutes=5)).isoformat(),
                "ValidUntilUtc": (now + timedelta(hours=1)).isoformat(), "Tenants": tenants,
                "Clients": [{"ClientId": client["id"], "RegistrationVersion": "1", "AllowSelfRevoke": True}
                            for client in clients]}},
        "Kestrel": {"Certificates": {"Default": certificate}},
        "Storage": {"Provider": "Local", "LocalRoot": str(root / "uploads")},
        "Security": {"Password": {"ExpiryDays": 0}},
        "ErpIntegration": {"Enabled": False},
        "SpaceObservability": {"MetricsEnabled": False},
        "Kafka": {"BootstrapServers": ""},
        "OperLog": {"IncludeGet": False},
    }


@contextmanager
def host(root, name, release, config, evidence, *, wrong_installation=False):
    content = root / name
    content.mkdir()
    directory = Path(release["directory"])
    settings = json.loads((directory / "appsettings.json").read_bytes())
    settings.update(config)
    profile = config["CrmIdentity"]["ServiceTokenTenancy"] | {"HostId": name}
    if wrong_installation:
        profile["InstallationId"] = str(uuid4())
    settings["CrmIdentity"] = config["CrmIdentity"] | {"ServiceTokenTenancy": profile}
    settings["Release"] = {"Version": "local-service-token-compat", "GitSha": release["sourceSha"],
                           "ApiImageDigest": "unknown", "WebImageDigest": "unknown"}
    write(content / "appsettings.json", settings)
    command = [str(DOTNET), str(directory / artifact.ENTRY), "--contentRoot", str(content)]
    env = isolated_environment() | {"ASPNETCORE_URLS": ISSUER + ":0"}
    run = {"sourceSha": release["sourceSha"], "processStopped": False}
    evidence[name] = run
    with (content / "stdout.log").open("wb") as output, (content / "stderr.log").open("wb") as error:
        process = subprocess.Popen(command, cwd=content, env=env, stdout=output, stderr=error,
                                   creationflags=pg.NO_WINDOW)
        port = None
        failed = False
        try:
            for _ in range(150):
                pg.require(process.poll() is None, f"{name} exited before listening; see private logs")
                match = re.search(r"Now listening on: https://127\.0\.0\.1:(\d+)",
                                  (content / "stdout.log").read_text(encoding="utf-8", errors="replace"))
                if match:
                    port = int(match[1])
                    break
                time.sleep(0.2)
            pg.require(port is not None, f"{name} did not listen; see private logs")
            observed = process_identity(process.pid)
            pg.require(Path(observed["Executable"]).resolve() == DOTNET.resolve()
                       and observed["Command"] == subprocess.list2cmdline(command)
                       and observed["Ports"] == [{"LocalAddress": "127.0.0.1", "LocalPort": port}],
                       f"{name} listener identity differs")
            run["process"] = observed
            yield f"{ISSUER}:{port}"
        except BaseException:
            failed = True
            raise
        finally:
            try:
                if process.poll() is None:
                    process.terminate()
                    process.wait(timeout=15)
                run["processStopped"] = process.poll() is not None
                if port is not None:
                    with socket.socket() as probe:
                        run["listenerClosed"] = probe.connect_ex(("127.0.0.1", port)) != 0
                    pg.require(run["listenerClosed"], f"{name} listener remained open")
            except Exception as error:
                run["cleanupFailure"] = type(error).__name__
                if not failed:
                    raise


def http(opener, evidence, label, base, path, expected, client=None, form=None):
    headers = {}
    if client:
        encoded = base64.b64encode(f'{client["id"]}:{client["secret"]}'.encode()).decode()
        headers["Authorization"] = "Basic " + encoded
    if form is not None:
        headers["Content-Type"] = "application/x-www-form-urlencoded"
    request = Request(base + path, urlencode(form).encode() if form is not None else None, headers)
    try:
        response = opener.open(request, timeout=15)
    except HTTPError as error:
        response = error
    with response:
        raw = response.read()
        body = json.loads(raw) if raw else None
        cache = response.headers.get("Cache-Control", "")
        evidence[label] = {"path": path, "status": response.status, "cacheControl": cache}
        if isinstance(body, dict) and "error" in body:
            evidence[label]["error"] = body["error"]
        pg.require(response.status == expected and "no-store" in cache.lower(),
                   f"{label} returned HTTP {response.status}; expected {expected} and no-store")
        return body


def token_row(state, jti):
    matches = [row for row in state["tokens"] if row["Jti"] == jti]
    pg.require(len(matches) == 1, f"Expected one exact token row for {jti}")
    return matches[0]


def issue(opener, report, control, label, base, client, before):
    body = http(opener, report["http"], label, base, "/connect/token", 200, client,
                {"grant_type": "client_credentials", "scope": "cp6.services"})
    encoded_header, encoded_claims, signature = body["access_token"].split(".")
    header, claims = [json.loads(base64.urlsafe_b64decode(part + "=" * (-len(part) % 4)))
                      for part in (encoded_header, encoded_claims)]
    jti = claims["jti"]
    after = observe(control)
    report["state"][label] = after
    row = token_row(after, jti)
    pg.require(signature and header == {"alg": "RS256", "kid": "w347", "typ": "at+jwt"}
               and claims["iss"] == row["Issuer"] == ISSUER and claims["aud"] == "CP6.Services"
               and claims["sub"] == "service:" + client["id"]
               and claims["tenant_id"] == row["TenantId"] == client["tenant"]
               and claims["client_id"] == row["ClientId"] == client["id"]
               and claims["scope"] == body["scope"] == "cp6.services"
               and row["RevokedAtUtc"] is None
               and datetime.fromisoformat(row["ExpiresAtUtc"]).timestamp() == claims["exp"],
               f"{label} JWT claims differ from the original issued row")
    pg.require(after | {"tokens": [item for item in after["tokens"] if item["Jti"] != jti]} == before,
               f"{label} changed state other than one new token")
    report["http"][label]["jti"] = jti
    return jti, after


def revoke(opener, report, label, base, client, jti):
    pg.require(http(opener, report["http"], label, base, "/connect/service-revocations",
                    200, client, {"jti": jti}) is None, f"{label} changed the empty-response contract")


def verify_revocation(before, after, tenant, jti):
    old, new = token_row(before, jti), token_row(after, jti)
    pg.require(old["RevokedAtUtc"] is None and new["RevokedAtUtc"] is not None
               and new["RowVersion"] != old["RowVersion"]
               and new | {"RevokedAtUtc": None, "RowVersion": old["RowVersion"]} == old,
               "The original token facts changed beyond revocation and row version")
    aggregate = f"token:{digest(ISSUER.encode())}:{jti}"
    snapshot, = [row for row in after["snapshots"] if row["AggregateId"] == aggregate]
    outbox, = [row for row in after["outbox"] if row["AggregateId"] == aggregate]
    raw = bytes.fromhex(outbox["Payload"].removeprefix("\\x"))
    envelope = json.loads(raw)
    payload = snapshot["PayloadJson"].encode()
    data = json.loads(payload)
    pg.require(snapshot["TenantId"] == outbox["TenantId"] == tenant
               and snapshot["Version"] == outbox["AggregateVersion"] == 1
               and snapshot["EventType"] == envelope["type"] == TOKEN_EVENT
               and envelope["aggregateid"] == aggregate and envelope["tenantid"] == tenant
               and envelope["data"] == data and data["jti"] == jti
               and data["issuer"] == old["Issuer"] and data["subjectId"] == "service:" + old["ClientId"]
               and datetime.fromisoformat(data["expiresAtUtc"]) == datetime.fromisoformat(old["ExpiresAtUtc"])
               and digest(payload) == snapshot["PayloadSha256"]
               and digest(raw) == outbox["PayloadSha256"],
               "The original token, snapshot, and priority event do not match")
    generations = before["generations"] | {tenant: before["generations"].get(tenant, 0) + 1}
    pg.require(after["generations"] == generations, "Wrong generation increment")
    pg.require(after | {
        "tokens": [old if row["Jti"] == jti else row for row in after["tokens"]],
        "snapshots": [row for row in after["snapshots"] if row["AggregateId"] != aggregate],
        "outbox": [row for row in after["outbox"] if row["AggregateId"] != aggregate],
        "generations": before["generations"],
    } == before, "Revocation affected unrelated rows")


def exercise(root, control, config, clients, releases, context, report):
    opener = build_opener(ProxyHandler({}), HTTPSHandler(context=context))
    a, b = clients
    processes, steps = report["processes"], report["http"]
    start = observe(control)
    report["state"]["initial"] = start
    with host(root, "prior", releases["prior"], config, processes) as prior:
        jti, first = issue(opener, report, control, "prior_issue", prior, a, start)
        with host(root, "current", releases["current"], config, processes) as current:
            for label, base, release in (("prior", prior, releases["prior"]),
                                         ("current", current, releases["current"])):
                metadata = http(opener, steps, label + "_release", base, "/health/release", 200)
                steps[label + "_release"]["body"] = metadata
                pg.require(metadata["gitSha"] == release["sourceSha"] and metadata["latestMigration"] is None,
                           "Unexpected release declaration or Core EF history")
            revoke(opener, report, "current_foreign_revoke", current, b, jti)
            foreign = observe(control)
            report["state"]["current_foreign_revoke"] = foreign
            pg.require(foreign == first, "Foreign client changed business state")
            revoke(opener, report, "current_revoke_prior", current, a, jti)
            revoked = observe(control)
            report["state"]["current_revoke_prior"] = revoked
            verify_revocation(first, revoked, a["tenant"], jti)
            next_jti, issued = issue(opener, report, control, "current_issue", current, a, revoked)
            report["ordinarySessionsDuringCoexistence"] = json.loads(pg.sql(control, """
                SELECT coalesce(json_agg(json_build_object('role',usename,'pid',pid,'clientPort',client_port)
                                         ORDER BY usename,pid),'[]')
                FROM pg_stat_activity WHERE datname=current_database() AND usename IN ('w347_tenant_a','w347_tenant_b');
                """, DATABASE))
            pg.require({row["role"] for row in report["ordinarySessionsDuringCoexistence"]}
                       == {"w347_tenant_a", "w347_tenant_b"}, "Ordinary tenant connections were not observed")
    with host(root, "prior_rollback", releases["prior"], config, processes) as prior:
        revoke(opener, report, "prior_revoke_current", prior, a, next_jti)
        final = observe(control)
        report["state"]["prior_revoke_current"] = final
        verify_revocation(issued, final, a["tenant"], next_jti)
        for label, original in (("prior_replay_current", next_jti), ("prior_replay_prior", jti)):
            revoke(opener, report, label, prior, a, original)
            repeated = observe(control)
            report["state"][label] = repeated
            pg.require(repeated == final, f"{label} changed the original rows or event bytes")
    with host(root, "current_wrong_installation", releases["current"], config,
              processes, wrong_installation=True) as wrong:
        rejected = http(opener, steps, "wrong_installation_issue", wrong, "/connect/token", 503, a,
                        {"grant_type": "client_credentials", "scope": "cp6.services"})
        actual = observe(control)
        report["state"]["wrong_installation_issue"] = actual
        pg.require(rejected["error"] == "temporarily_unavailable" and actual == final,
                   "Wrong installation unexpectedly changed business state")


def stop_database(root, control):
    pid_file = root / "data/postmaster.pid"
    if control is not None:
        pg.stop(control)
    elif pid_file.is_file():
        # A failed create may leave a cluster before it creates the guarded management database.
        partial = json.loads((root / "settings-before-start.json").read_bytes())
        pg.identity(partial, "postgres")
        pg.zero_sessions(partial, "postgres")
        pg.run([pg.BIN / "pg_ctl.exe", "-D", partial["DataDirectory"], "-m", "smart", "-w", "-t", "15", "stop"])
    pg.require(not pid_file.exists(), "Owned PostgreSQL process remains")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("publish-root", "package-cache", "tenant-storage"):
        parser.add_argument("--" + name, required=True, type=Path)
    args = parser.parse_args()
    releases = verified_releases(args)
    owner = uuid4().hex
    root = pg.ROOT / owner
    print(f"W347 private evidence: {root}", flush=True)
    report = {"completed": False, "startedUtc": datetime.now(timezone.utc).isoformat(),
              "driverDeps": DRIVERS, "releases": releases, "processes": {}, "http": {}, "state": {},
              "tenantStorageSha256": digest(args.tenant_storage.read_bytes())}
    control = None
    checked = False
    try:
        pg.create(owner)
        control = pg.load_settings(root / "settings.json")
        request, tenants = install(control, args.tenant_storage.resolve(), root, report)
        report["catalogBefore"] = catalog(control)
        initial = report["catalogBefore"]
        pg.require(initial["profile"] == "service-token-v1" and initial["fingerprint"] == initial["liveFingerprint"]
                   and initial["baselineSha256"] == report["installation"]["BaselineSha256"]
                   and initial["tokenRlsForced"] and not initial["defaultMayWriteToken"]
                   and initial["efHistoryTables"] == 0, "Unexpected service-token catalog")
        clients = [{"id": "w347-" + tag, "tenant": tenant["TenantId"], "secret": secrets.token_hex(24)}
                   for tag, tenant in zip(("a", "b"), tenants)]
        signing, certificate, context = issue_certificate(root)
        config = configuration(control, request, tenants, clients, signing, certificate, root)
        exercise(root, control, config, clients, releases, context, report)
        report["catalogAfter"] = catalog(control)
        pg.require(report["catalogAfter"] == initial, "The verified layout changed")
        pg.require(verified_releases(args) == releases, "The published inputs changed")
        checked = True
    except BaseException as error:
        report["failure"] = type(error).__name__
        raise
    finally:
        try:
            stop_database(root, control)
            report["postgresStopped"] = control is not None
        except Exception as error:
            report["cleanupFailure"] = type(error).__name__
            if "failure" not in report:
                raise
        finally:
            report["completed"] = (checked and report.get("postgresStopped", False)
                                   and all(entry["processStopped"] and entry.get("listenerClosed", False)
                                           for entry in report["processes"].values()))
            report["finishedUtc"] = datetime.now(timezone.utc).isoformat()
            if root.is_dir():
                write(root / "w347-results.json", report)
    pg.require(report["completed"], "Compatibility validation incomplete")
    print("W347 actual published service-token compatibility passed")


if __name__ == "__main__":
    main()
