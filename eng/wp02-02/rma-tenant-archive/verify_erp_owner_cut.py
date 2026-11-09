#!/usr/bin/env python3
"""Create real C03 Owner originals and compare a normal v10 cut on an owned source copy."""
import argparse
import csv
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
from uuid import uuid4

sys.dont_write_bytecode = True
import archive
import erp_integration as erp

HERE = Path(__file__).resolve().parent
Q = archive.literal
FAILURE = 'C03_BUSINESS_PARTNER_NOT_FOUND'
RESULT_TYPE = 'com.gtx537.erp.business-partner-failed.v1'
REQUEST_TYPE = 'com.gtx537.crm.erp.business-partner-requested.v1'
NONEMPTY = {'Aggregate', 'CommandInbox', 'Cp6_OutboxMessage', 'Request'}
V10 = ('--include-original-journal', '--include-rma-source', '--include-rma-visibility',
       '--include-identity-priority', '--include-rma-iam-references', '--include-erp-integration',
       '--include-privacy-evidence', '--include-main-stock-outbox')


def require(value, error):
    if not value:
        raise ValueError(error)


def save(path, content):
    with path.open('x', encoding='utf-8', newline='\n') as out:
        json.dump(content, out, indent=2)
        out.write('\n')


def child(output, label, command, env, *, statement=None, timeout=35):
    options = dict(stdin=subprocess.DEVNULL) if statement is None else dict(input=statement.encode('utf-8'))
    with (output / (label + '.stdout')).open('xb') as out, (output / (label + '.stderr')).open('xb') as err:
        result = subprocess.run(list(map(str, command)), cwd=output, env=env, stdout=out, stderr=err,
                                creationflags=archive.FLAGS, timeout=timeout, **options)
    save(output / (label + '.exit.json'), dict(exit=result.returncode))
    require(result.returncode == 0, label + ' failed, see native evidence')
    return (output / (label + '.stdout')).read_bytes()


class Fixture:
    def __init__(self, args, target, output):
        self.args, self.target, self.output = args, target, output
        self.env = {k: v for k, v in os.environ.items()
                    if not k.upper().startswith(('PG', 'DOTNET_', 'COMPLUS_', 'COREHOST_', 'ASPNETCORE_'))}
        self.tenants = tuple(str(tenant) for tenant in (args.tenant_a, args.tenant_b))
        self.psql = [archive.binary(args.pg_bin, 'psql'), '-XqAtw', '-v', 'ON_ERROR_STOP=1', '-v', 'VERBOSITY=verbose',
                     *archive.connection_args(target, args.username)]

    def sql(self, label, body):
        return child(self.output, label, self.psql, archive.client_environment(self.env, 'w419-' + label),
                     statement="BEGIN READ ONLY; SET LOCAL row_security=off; SET LOCAL TIME ZONE 'UTC'; "
                               "SET LOCAL DateStyle='ISO, YMD'; SET LOCAL bytea_output='hex';\n" + body + '\nCOMMIT;')

    def snapshot_erp(self, label, tenant):
        return {table.name: self.sql(f'{label}-{table.member}',
            f'COPY ({erp.rows_sql(table, tenant)}) TO STDOUT WITH (FORMAT csv, HEADER);') for table in erp.TABLES}

    def protect_other_owners(self, label):
        a, b = self.tenants
        scope = f'{Q(a)}::uuid,{Q(b)}::uuid'
        items = {
            'partners': f'SELECT * FROM public."T_WebBusinessPartner" WHERE "TenantId" IN ({scope})',
            'originals': f'SELECT * FROM {archive.JOURNAL_TABLE} WHERE tenant_id IN ({scope})',
        }
        stock = archive.stock_outbox_module()
        for tenant in (a, b):
            schema = 't_' + archive.guid(tenant).hex
            for table in ('heads', 'versions'):
                items[tenant + '/' + table] = f'SELECT * FROM {schema}.{table}'
            root = stock.root(tenant)['id']
            for table in stock.TABLES:
                items[tenant + '/stock/' + table.name] = stock.rows_sql(table, tenant, root, ordered=False)
        columns = ',\n'.join(f"{Q(name)},(SELECT jsonb_build_array(count(*),encode(sha256(convert_to("
                              f"coalesce(jsonb_agg(to_jsonb(r) ORDER BY to_jsonb(r)),'[]'::jsonb)::text,'UTF8')),'hex'))"
                              f" FROM ({query}) r)" for name, query in items.items())
        return self.sql(label, 'SELECT jsonb_build_object(' + columns + ');')

    def source_cut(self, target_file):
        archive_cli = [sys.executable, '-B', HERE / 'archive.py']
        bundle = self.output / 'v10-cut/bundle'
        tenant = self.tenants[0]
        args = self.args
        child(self.output, 'archive-export', [*archive_cli, 'export', '--pg-bin', args.pg_bin,
              '--target-file', target_file, '--username', args.username, '--installation', args.installation,
              '--tenant', tenant, '--file-content-root', args.file_content_root, '--output', bundle.parent, *V10],
              self.env, timeout=90)
        child(self.output, 'archive-verify', [*archive_cli, 'verify', '--bundle', bundle,
              '--tenant', tenant, '--file-content-root', args.file_content_root], self.env, timeout=45)
        return bundle, json.loads((bundle / 'manifest.json').read_bytes())


def csv_rows(raw, table):
    stream = io.StringIO(raw.decode('utf-8'), newline='')
    reader = csv.DictReader(stream, strict=True)
    require(tuple(reader.fieldnames or ()) == table.columns, table.name + ' native columns differ')
    return list(reader)


def native_rows(slices, *, expect_empty=False):
    rows = {table.name: csv_rows(slices[table.name], table) for table in erp.TABLES}
    for name, records in rows.items():
        expected = 1 if not expect_empty and name in NONEMPTY else 0
        require(len(records) == expected, name + ' native row count differs')
    return rows


def hex_bytes(text):
    require(text.startswith('\\x'), 'native bytea was not hex')
    return bytes.fromhex(text[2:])


def sha(data):
    return hashlib.sha256(data).hexdigest()


def check_erp(rows, tenant, managed, request_id, account_id):
    aggregate, inbox, outbox, request = (rows[name][0] for name in ('Aggregate', 'CommandInbox', 'Cp6_OutboxMessage', 'Request'))
    require(managed['Tenant'] == tenant and managed['First'] == 'Applied' and managed['Duplicate'] == 'Duplicate',
            'managed result does not identify the exact original tenant and duplicate')
    for row in (aggregate, inbox, outbox, request):
        token = hex_bytes(row['RowVersion'])
        require(row['TenantId'] == tenant and row['AggregateId'] == account_id
                and len(token) == 8 and int.from_bytes(token, 'big') > 0, 'native tenant, business key or token differs')
    require((aggregate['Kind'], aggregate['LastRequestVersion'], aggregate['LastAggregateVersion'],
             aggregate['LastResultVersion']) == ('1', '3', '7', '1'), 'native aggregate lineage differs')
    payload = hex_bytes(inbox['Payload'])
    command = json.loads(payload)
    require(inbox['MessageId'] == managed['CommandId'] == command['id'] and inbox['EventType'] == command['type'] == REQUEST_TYPE
            and inbox['PayloadSha256'] == managed['CommandSha256'] == sha(payload)
            and inbox['Status'] == '1' and inbox['AggregateVersion'] == '7' and inbox['AttemptCount'] == '1' and inbox['ProcessedAtUtc']
            and not inbox['ErrorCode'] and not inbox['RetryAtUtc']
            and command['data'] == dict(tenantId=tenant, requestId=request_id, accountId=account_id, requestVersion=3),
            'native Processed Inbox or original command bytes differ')
    original_result = hex_bytes(outbox['Payload'])
    event = json.loads(original_result)
    original_data = event['data']
    require(outbox['MessageId'] == managed['ResultId'] == event['id'] and outbox['CausationId'] == managed['CommandId']
            and outbox['PayloadSha256'] == managed['ResultSha256'] == sha(original_result)
            and outbox['Status'] == '0' and outbox['AggregateVersion'] == '1' and outbox['AttemptCount'] == '0'
            and not any(outbox[field] for field in ('PublishedAtUtc', 'LeaseToken', 'LeaseOwner', 'LeaseExpiresAtUtc'))
            and event['type'] == RESULT_TYPE and original_data == managed['Result'],
            'native Pending Outbox or original event bytes differ')
    require(request['Kind'] == '1' and request['RequestVersion'] == '3' and request['RequestId'] == request_id
            and request['AccountId'] == account_id and request['Terminal'] == 't' and request['Succeeded'] == 'f'
            and request['ResultVersion'] == '1' and request['ResultType'] == RESULT_TYPE
            and request['InputSha256'] == managed['InputSha256']
            and json.loads(request['ResultDataJson']) == original_data
            and original_data == dict(tenantId=tenant, requestId=request_id, accountId=account_id,
                                      requestVersion=3, resultVersion=1, errorCode=FAILURE, retryable=False),
            'native terminal Request binding or raw result does not agree with the actual C03 event')
    return dict(command=command['id'], commandSha256=sha(payload), result=event['id'], resultSha256=sha(original_result),
                tokens={name: records[0]['RowVersion'] for name, records in rows.items() if records})


def guarded_fixture(args):
    receipt = json.loads(args.copy_receipt.read_bytes())
    source, copy = (Path(receipt[name]) for name in ('source', 'data'))
    require(source.resolve(strict=True) == source and copy.resolve(strict=True) == copy
            and source != copy and source not in copy.parents and copy not in source.parents
            and not (source / 'postmaster.pid').exists(), 'isolated stopped-source copy is required')
    target = archive.PhysicalTarget.from_mapping(receipt['physical'])
    require(args.output.name not in ('', '.', '..'), 'new private output required')
    output = args.output.parent.resolve(strict=True) / args.output.name
    require(output not in (source, copy) and source not in output.parents and copy not in output.parents,
            'private results must be outside PostgreSQL data')
    require(args.tenant_a != args.tenant_b and args.file_content_root.is_dir()
            and not any(args.file_content_root.iterdir()), 'distinct tenants and original empty file root are required')
    archive.private_directory(output)
    fixture = Fixture(args, target, output)
    identity = json.loads(fixture.sql('exact-source', """
        SELECT jsonb_build_object('target',jsonb_build_object('Server',host(inet_server_addr()),'Port',inet_server_port(),
           'Database',d.datname,'DatabaseOid',d.oid::bigint,'SystemIdentifier',s.system_identifier::text),
           'data',current_setting('data_directory'),'owner',pg_get_userbyid(d.datdba),'session',session_user,
           'installation',(SELECT installation_id FROM cp6_rma_tenant_v1.installation WHERE singleton),
           'readOnly',current_setting('default_transaction_read_only'),'recovery',pg_is_in_recovery(),
           'erpMigration',EXISTS(SELECT FROM public."__EFMigrationsHistory_ErpIntegration"
             WHERE "MigrationId"='20261002151345_PostgreSqlErpIntegrationBaselineV1'))
        FROM pg_database d CROSS JOIN pg_control_system() s WHERE datname=current_database();
    """))
    require(identity == dict(target=target.mapping(), data=copy.as_posix(), owner=args.username, session=args.username,
              installation=str(args.installation), readOnly='on', recovery=False, erpMigration=True), 'wrong actual source identity')
    return fixture, copy


def execute(args, invoke=child):
    f, copy = guarded_fixture(args)
    other_before = f.protect_other_owners('other-owners-before')
    for label, tenant in zip(('a', 'b'), f.tenants):
        native_rows(f.snapshot_erp('before-' + label, tenant), expect_empty=True)
    request_id, account_id = str(uuid4()), str(uuid4())
    driver_input = f.output / 'driver-input.json'
    pins = json.loads(args.module_pins.read_bytes())
    save(driver_input, dict(DataDirectory=copy.as_posix(), Database=f.target.database, Port=f.target.port,
        DatabaseOid=f.target.database_oid, SystemIdentifier=f.target.system_identifier, Username=args.username,
        BinaryRoot=args.sealed_root.resolve(strict=True).as_posix(), Installation=str(args.installation),
        TenantA=f.tenants[0], TenantB=f.tenants[1], RequestId=request_id, AccountId=account_id, ModulePins=pins))
    command = [args.dotnet, 'exec', '--runtimeconfig', args.runtime_config, '--depsfile',
               args.sealed_root / 'CP6.Tests.deps.json', args.driver, driver_input]
    managed = json.loads(invoke(f.output, 'erp-owner', command, f.env))
    require(managed['RequestId'] == request_id and managed['AccountId'] == account_id
            and managed['RequestVersion'] == 3 and managed['AggregateVersion'] == 7, 'managed input differs')
    after = {label: f.snapshot_erp('native-' + label.lower(), tenant) for label, tenant in zip(('A', 'B'), f.tenants)}
    verified = {label: check_erp(native_rows(after[label]), tenant, managed[label], request_id, account_id)
                for label, tenant in zip(('A', 'B'), f.tenants)}
    require(verified['A']['command'] != verified['B']['command'] and verified['A']['result'] != verified['B']['result'],
            'global command or result IDs collided between tenants')
    target_file = f.output / 'source-target.json'
    save(target_file, f.target.mapping())
    bundle, manifest = f.source_cut(target_file)
    require(manifest['format'] == archive.FORMAT_WITH_STOCK_OUTBOX and manifest['tenantId'] == f.tenants[0]
            and manifest['source'] == f.target.mapping() and manifest['installationId'] == str(args.installation)
            and manifest['fileContentAssets']['count'] == 0 and manifest['recoveryReady'] is False,
            'actual normal cut identity differs')
    for table in erp.TABLES:
        require(manifest['erpIntegration']['tables'][table.name]['count'] == (table.name in NONEMPTY)
                and (bundle / table.member).read_bytes() == after['A'][table.name], 'native A cut differs for ' + table.name)
    require(after['B'] == f.snapshot_erp('after-cut-b', f.tenants[1])
            and other_before == f.protect_other_owners('other-owners-after'), 'B or other original domains changed')
    result = dict(target=f.target.mapping(), installationId=str(args.installation), manifest=archive.file_digest(bundle / 'manifest.json'),
                  bundle=bundle.as_posix(), snapshot=manifest['snapshot'], tenants=f.tenants, request=request_id, account=account_id,
                  native=verified, erpCounts={t.name: int(t.name in NONEMPTY) for t in erp.TABLES},
                  otherOwners=archive.file_digest(f.output / 'other-owners-before.stdout'),
                  duplicateSnapshotSha256=managed['DuplicateSnapshotSha256'], backend=managed['Backend'], recoveryReady=False)
    save(f.output / 'actual.json', result)
    print(json.dumps(result, indent=2), flush=True)
    return result


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    for field in ('pg-bin', 'copy-receipt', 'file-content-root', 'output', 'dotnet', 'driver', 'sealed-root', 'runtime-config', 'module-pins'):
        result.add_argument('--' + field, type=Path, required=True)
    for field in ('installation', 'tenant-a', 'tenant-b'):
        result.add_argument('--' + field, type=archive.guid, required=True)
    result.add_argument('--username', required=True)
    return result


def main():
    try:
        execute(parser().parse_args())
    except (archive.ArchiveError, OSError, ValueError, KeyError, subprocess.SubprocessError) as error:
        print('ERP Owner cut failed:', error, file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
