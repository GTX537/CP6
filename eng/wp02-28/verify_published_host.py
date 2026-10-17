"""Start only a pinned dotnet-publish output; check actual local process identity.

Diagnostic startup can pass with unloaded assemblies, but identity readiness then
remains Unknown/false. This does not prove business readiness or release fitness.
"""
import argparse
import datetime
import json
import os
from pathlib import Path
import re
import secrets
import socket
import subprocess
import time
import urllib.error
import urllib.request
import uuid

import pg_resource as pg
import published_artifact as artifact
from verify_host import DOTNET, ENDPOINT, process_identity, token, write


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def request(url, bearer=None):
    headers = {'Authorization': 'Bearer ' + bearer} if bearer else {}
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    try:
        response = opener.open(urllib.request.Request(url, headers=headers), timeout=12)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        raw = response.read()
        pg.require(response.status == 200 and response.headers.get('Cache-Control') == 'no-store',
                   'Identity/release endpoint denied or not no-store')
        return json.loads(raw), raw


def identity_readiness(body, expected, native):
    pg.require(body['processId'] == native['Pid'], 'Endpoint PID mismatch')
    started = datetime.datetime.fromisoformat(body['processStartedAtUtc'])
    observed = datetime.datetime.fromisoformat(native['Started'])
    pg.require(abs((started - observed).total_seconds()) < .001, 'Endpoint process start mismatch')
    modules = {m['packageId']: m for m in body['modules']}
    pg.require(len(modules) == len(body['modules']) == len(expected)
               and set(modules) == {e['PackageId'] for e in expected}, 'Endpoint module set mismatch')
    loaded = 0
    for e in expected:
        module = modules[e['PackageId']]
        expected_json = {k[0].lower() + k[1:]: v for k, v in e.items() if k != 'PackageId'}
        pg.require(module['expected'] == expected_json, 'Endpoint expected identity mismatch')
        o = module['observed']
        if o is None:
            pg.require(module['status'] == 'Unknown' and module['reasonCodes'] == ['ASSEMBLY_NOT_LOADED'],
                       'Unobserved assembly has invalid readiness')
        else:
            loaded += 1
            pg.require(module['status'] == 'Matched' and not module['reasonCodes']
                       and o['assemblySha256'] == e['AssemblySha256'] and o['sourceSha'] == e['SourceSha']
                       and o['declaredPackageVersion'] == e['PackageVersion'] and o['defaultLoadContext']
                       and o['loadedMvid'] and o['loadedMvid'] == o['backingMvid'],
                       'Loaded assembly identity mismatch or unavailable')
    pg.require(loaded > 0, 'No Platform assembly observed')
    status = 'Matched' if loaded == len(expected) else 'Unknown'
    pg.require(body['identityStatus'] == status and not body['reasonCodes'], 'Aggregate identity mismatch')
    pg.require(body['rollback']['allowed'] is False and all(body[k] == 'Unknown'
               for k in ('runtimeQualification', 'businessAcceptance', 'trustState')), 'Unexpected qualification claim')
    return {'status': status, 'ready': status == 'Matched', 'loadedCount': loaded,
            'unloadedCount': len(expected) - loaded}


def isolated_environment():
    # A fresh configuration root must not inherit .NET loaders, startup hooks,
    # arbitrary ASP.NET configuration, proxy routing or user-secret overrides.
    allowed = {'SYSTEMROOT', 'WINDIR', 'COMSPEC', 'PATH', 'PATHEXT', 'TEMP', 'TMP',
               'USERPROFILE', 'APPDATA', 'LOCALAPPDATA', 'PROGRAMFILES', 'PROGRAMFILES(X86)', 'PROGRAMDATA'}
    env = {k: v for k, v in os.environ.items() if k.upper() in allowed}
    env.update(ASPNETCORE_ENVIRONMENT='Development', DOTNET_ENVIRONMENT='Development',
               ASPNETCORE_URLS='http://127.0.0.1:0', DOTNET_NOLOGO='1')
    return env


def negative_checks(directory, verify):
    """Mutate only the fresh owned output, before any host exists; always restore."""
    outcomes = []
    for name, file, mutation in (
        ('missing-runtimeconfig', artifact.RUNTIME, None),
        ('platform-identity-tamper', 'CP6.Platform.AspNetCore.dll', b'wrong-published-identity'),
    ):
        path = directory / file
        original = path.read_bytes()
        try:
            if mutation is None:
                path.unlink()
            else:
                path.write_bytes(original + mutation)
            try:
                verify()
            except RuntimeError as error:
                outcomes.append({'scenario': name, 'rejectedBeforeStart': True, 'reason': str(error)})
            else:
                raise RuntimeError('Invalid publish output accepted: ' + name)
        finally:
            path.write_bytes(original)
        verify()
    return outcomes


def run(args):
    c = pg.load_settings(args.settings)
    pg.require(c['Owner'] == args.owner, 'Explicit owner mismatch')
    directory = Path(args.publish_directory).absolute()
    pg.require(directory.parent == pg.validate_paths(c), 'Publish path is outside the owned root')
    verify = lambda: artifact.verify(directory, args.receipt, args.receipt_sha256, args.source_sha, args.package_cache)
    receipt = verify()  # No database writes, runtime configuration or process until preflight passes.
    owned = pg.verify(c)
    runroot = Path(c['RunDirectory']) / ('published-host-' + datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ'))
    runroot.mkdir()
    results = {'sourceSha': args.source_sha, 'receiptSha256': args.receipt_sha256,
               'entrySha256': receipt['files'][artifact.ENTRY], 'completed': False,
               'diagnosticStartupPassed': False, 'identityReadiness': {'status': 'Unknown', 'ready': False},
               'scope': 'actual published local process, minimal authorization fixture; no business readiness',
               'databaseIdentity': {k: owned[k] for k in ('SystemIdentifier', 'Database', 'Oid', 'Owner', 'Port')}}
    process = native = port = None
    output = error = None
    try:
        if args.negative_checks:
            results['negativeChecks'] = negative_checks(directory, verify)
        key, user = secrets.token_hex(32), str(uuid.uuid4())
        pg.sql(c, 'CREATE TABLE IF NOT EXISTS public."Sys_Users" ("Id" uuid PRIMARY KEY,"IsPlatformAdmin" boolean NOT NULL,"Enable" boolean NOT NULL);'
               f'INSERT INTO public."Sys_Users" VALUES (\'{user}\',true,true);'
               f'GRANT USAGE ON SCHEMA public TO "{c["Runtime"]}"; GRANT SELECT ON public."Sys_Users" TO "{c["Runtime"]}";'
               f'ALTER ROLE "{c["Runtime"]}" SET default_transaction_read_only=on;')
        config = json.loads((directory / 'appsettings.json').read_bytes())
        config.update({'Startup': {'SkipDatabaseInitialization': True, 'SkipHostedServices': True},
                       'Database': {'Provider': 'PostgreSql'}, 'JWT': {'Issuer': 'CP6', 'Audience': 'CP6.Web', 'Secret': key},
                       'ConnectionStrings': {'DefaultConnection': f'Host=127.0.0.1;Port={pg.PORT};Database={c["Database"]};Username={c["Runtime"]};Password={c["RuntimePassword"]};Timeout=3', 'Redis': ''},
                       'CrmOidc': {'Enabled': False}, 'CrmIdentity': {'Enabled': False}, 'ErpIntegration': {'Enabled': False},
                       'SpaceObservability': {'MetricsEnabled': False}, 'OperLog': {'IncludeGet': False},
                       'Storage': {'Provider': 'Local', 'LocalRoot': str(runroot / 'uploads')},
                       'Release': {'Version': 'local-publish-diagnostic', 'GitSha': args.source_sha,
                                   'ApiImageDigest': 'unknown', 'WebImageDigest': 'unknown'},
                       'PlatformHostIdentity': {'ExpectedModules': receipt['expectedModules']}})
        write(runroot / 'appsettings.json', config)
        output, error = (runroot / 'stdout.log').open('wb'), (runroot / 'stderr.log').open('wb')
        verify()  # Check again immediately before starting the exact entry from the receipt.
        command = [str(DOTNET), str(directory / artifact.ENTRY), '--contentRoot', str(runroot)]
        process = subprocess.Popen(command, cwd=runroot, env=isolated_environment(), stdout=output,
                                   stderr=error, creationflags=pg.NO_WINDOW)
        native = process_identity(process.pid)
        pg.require(Path(native['Executable']).resolve() == DOTNET.resolve()
                   and subprocess.list2cmdline(command) == native['Command'], 'OS process entry/command mismatch')
        results['process'] = native
        for _ in range(150):
            pg.require(process.poll() is None, 'Published host exited before listening; private logs retained')
            text = (runroot / 'stdout.log').read_text(encoding='utf-8-sig', errors='replace')
            match = re.search(r'Now listening on: http://127\.0\.0\.1:(\d+)', text)
            if match:
                port = int(match.group(1))
                break
            time.sleep(.2)
        pg.require(port is not None, 'Published host startup timed out')
        listening = process_identity(process.pid)
        pg.require(listening['Started'] == native['Started'] and listening['Command'] == native['Command']
                   and listening['Ports'] == [{'LocalAddress': '127.0.0.1', 'LocalPort': port}], 'OS listener identity mismatch')
        results['process'] = listening
        base = 'http://127.0.0.1:' + str(port)
        body, raw = request(base + ENDPOINT, token(key, user))
        results['platformIdentity'] = body
        results['identityReadiness'] = identity_readiness(body, receipt['expectedModules'], native)
        release, release_raw = request(base + '/health/release')
        results['release'] = release
        pg.require(release == {'version': 'local-publish-diagnostic', 'gitSha': args.source_sha,
                   'apiImageDigest': 'unknown', 'webImageDigest': 'unknown', 'latestMigration': None}, 'Release contract mismatch')
        pg.require(all(secret.encode() not in raw + release_raw for secret in
                   (key, c['RuntimePassword'], c['AdminPassword'], str(runroot), c['Database'])), 'Sensitive endpoint output')
        verify()  # Runtime content root must not mutate the publish closure.
        results['diagnosticStartupPassed'] = True
    except Exception as exc:
        # Do not serialize provider diagnostics, config or credentials.
        results['failureType'] = type(exc).__name__
        raise
    finally:
        try:
            if process is not None:
                if process.poll() is None:
                    current = process_identity(process.pid)
                    pg.require(native is not None and current['Started'] == native['Started']
                               and current['Command'] == native['Command'], 'Refuse stop: process identity changed')
                    process.terminate()
                    process.wait(timeout=15)
                results['processStopped'] = process.poll() is not None
                if port:
                    with socket.socket() as probe:
                        results['listenerClosed'] = probe.connect_ex(('127.0.0.1', port)) != 0
                    pg.require(results['listenerClosed'], 'Owned HTTP port still listening')
                results['completed'] = (results['diagnosticStartupPassed']
                                        and results['processStopped'] and results.get('listenerClosed', False))
        except Exception as exc:
            results['cleanupFailureType'] = type(exc).__name__
            raise
        finally:
            if output: output.close()
            if error: error.close()
            write(runroot / 'results.json', results)
    print('Local published diagnostic completed; identity readiness=' + results['identityReadiness']['status']
          + '; results=' + str(runroot / 'results.json'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('owner', 'settings', 'publish-directory', 'receipt', 'receipt-sha256', 'source-sha', 'package-cache'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--negative-checks', action='store_true')
    run(parser.parse_args())


if __name__ == '__main__':
    main()
