#!/usr/bin/env python3
"""Verify the original W419 A graph on an isolated native candidate with retained B originals."""
import argparse
from dataclasses import dataclass, replace
import json
import os
from pathlib import Path
import subprocess
import sys
from unittest.mock import patch
from uuid import uuid4

sys.dont_write_bytecode = True
import archive
import erp_terminal_candidate as importer
import stage
from verify_erp_owner_cut import Fixture, child, csv_rows, require, save

CLI = Path(importer.__file__).resolve()
Q = archive.literal


@dataclass(frozen=True)
class Inputs:
    pg_bin: Path
    target_file: Path
    username: str
    tenant: str
    candidate_id: str
    manifest_sha256: str
    bundle: Path
    file_content_root: Path
    output: Path
    action: str = 'import'
    include_file_content_history: bool = False


def call(f, label, args, *, error=None, discard=False):
    command = [sys.executable, '-B', str(CLI), args.action]
    fields = ('pg_bin', 'target_file', 'username', 'tenant', 'candidate_id', 'manifest_sha256', 'output')
    if args.action == 'import':
        fields += ('bundle', 'file_content_root')
    for field in fields:
        command += ['--' + field.replace('_', '-'), str(getattr(args, field))]
    if args.action == 'import' and args.include_file_content_history:
        command.append('--include-file-content-history')
    result = subprocess.run(command, cwd=f.output, env=f.env, stdin=subprocess.DEVNULL,
                            stdout=subprocess.DEVNULL if discard else subprocess.PIPE, stderr=subprocess.PIPE,
                            creationflags=archive.FLAGS, timeout=90)
    if not discard:
        (f.output / (label + '.stdout')).write_bytes(result.stdout)
    (f.output / (label + '.stderr')).write_bytes(result.stderr)
    save(f.output / (label + '.exit.json'), dict(exit=result.returncode, discarded=discard))
    if error is None:
        require(result.returncode == 0 and not result.stderr, label + ' native command failed')
    else:
        require(result.returncode == 1 and not result.stdout and error.encode('ascii') in result.stderr,
                label + ' wrong rejection')
    return result.stdout


def boundary(f, label):
    return f.sql(label, f"""
        SELECT jsonb_build_object(
           'sequence',(SELECT jsonb_build_array(last_value,is_called) FROM {importer.SEQUENCE}),
           'triggers',(SELECT jsonb_agg(jsonb_build_array(t.oid,t.tgenabled,pg_get_triggerdef(t.oid),t.tgfoid)
                          ORDER BY t.tgrelid,t.oid) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
                          WHERE c.relnamespace='erp_integration'::regnamespace),
           'acl',(SELECT jsonb_agg(jsonb_build_array(c.oid,c.relowner,c.relacl,c.relrowsecurity) ORDER BY c.oid)
                    FROM pg_class c WHERE c.relnamespace IN ('erp_integration'::regnamespace,'cp6_storage_erp'::regnamespace)),
           'schemaAcl',(SELECT jsonb_agg(jsonb_build_array(nspname,nspowner,nspacl) ORDER BY nspname)
                        FROM pg_namespace WHERE nspname IN ('erp_integration','cp6_storage_erp')),
           'dbAcl',(SELECT datacl FROM pg_database WHERE datname=current_database()),
           'admission',(SELECT count(*) FROM cp6_privacy_disposition_v1.restore_admission),
           'environment',(SELECT count(*) FROM cp6_scope_authorization_v1.environment),
           'receipt',to_regnamespace('{importer.RECEIPTS}') IS NOT NULL);
    """)


def inject(f, args, marker, sql, expected):
    original = stage.run_driver

    def fault(pg, physical, username, output, action):
        driver = output / 'driver.sql'
        text = driver.read_text(encoding='utf-8')
        require(text.count(marker) == 1, 'ERP original fault point changed')
        driver.write_text(text.replace(marker, sql + marker), encoding='utf-8')
        return original(pg, physical, username, output, action)

    with patch.dict(os.environ, f.env, clear=True), patch.object(stage, 'run_driver', side_effect=fault):
        try:
            importer.execute(args)
        except stage.StageError as error:
            require(str(error) == expected, 'unexpected native fault result')
        else:
            raise ValueError('injected ERP fault committed')
    return (args.output / 'stage.stderr').read_bytes()


def inject_unique_collision(f, args, a, b_message):
    target = importer.relation('Cp6_OutboxMessage')
    trigger = f'ALTER TABLE ONLY {target} DISABLE TRIGGER cp6_rowversion_v1;'
    inserted = ' AND '.join(f'(SELECT count(*) FROM {importer.relation(name)} WHERE "TenantId"={Q(a)}::uuid)=1'
                            for name in importer.NAMES[:-1])
    injected = f"""
    DO $w422_late$
    BEGIN
        IF NOT ({inserted}) OR NOT EXISTS (SELECT FROM {importer.SEQUENCE} WHERE last_value=11 AND NOT is_called) THEN
            RAISE EXCEPTION 'fixture did not reach A writes and transactional sequence restart';
        END IF;
        RAISE NOTICE 'fixture observed three A originals and next token 11 before Outbox native key check';
    END $w422_late$;
    UPDATE {importer.temporary('Cp6_OutboxMessage')} SET "MessageId"=upper({Q(b_message)})||'  ';
    """

    evidence = inject(f, args, trigger, injected, 'rma-stage-native-failed (see native logs)')
    require(b'fixture observed three A originals and next token 11' in evidence and b'23505' in evidence
            and b'IX_Cp6_OutboxMessage_MessageId' in evidence, 'wrong native Outbox global collision evidence')


def protected_expected(root, tenant, prefix):
    result = {}
    for name, table in importer.TABLES.items():
        path = root / (prefix + table.member + ('.stdout' if prefix else ''))
        result[name] = path.read_bytes()
        rows = csv_rows(result[name], table)
        require(len(rows) == importer.COUNTS[name] and all(row['TenantId'] == tenant for row in rows),
                'wrong original tenant/count for ' + name)
    return result


def exercise(f, args, old_bundle, old_pin, old_assets, b_original, source_file):
    a, b = f.tenants
    a_expected = protected_expected(args.bundle, a, '')
    b_expected = protected_expected(b_original, b, 'native-b-')
    b_message = csv_rows(b_expected['Cp6_OutboxMessage'], importer.TABLES['Cp6_OutboxMessage'])[0]['MessageId']
    a_request = csv_rows(a_expected['Request'], importer.TABLES['Request'])[0]
    b_request = csv_rows(b_expected['Request'], importer.TABLES['Request'])[0]
    require((a_request['RequestId'], a_request['AccountId']) == (b_request['RequestId'], b_request['AccountId']),
            'actual W419 business-key control differs')
    baseline = boundary(f, 'boundary-before')
    controls = json.loads(baseline)
    require(controls['sequence'] == [1, False] and controls['admission'] == controls['environment'] == 0
            and controls['receipt'] is False and len(controls['triggers']) == 6, 'wrong native empty target boundary')
    empty = f.snapshot_erp('a-before', a)
    require(all(not csv_rows(raw, importer.TABLES[name]) for name, raw in empty.items())
            and f.snapshot_erp('b-before', b) == b_expected, 'not the actual empty A and retained B target')

    def attempt(label, **kwargs):
        return replace(args, output=f.output / label, **kwargs)

    call(f, 'source-target', attempt('source-target', target_file=source_file), error='rma-stage-target-is-source')
    call(f, 'wrong-marker', attempt('wrong-marker', candidate_id=str(uuid4())), error='rma-stage-candidate-marker-rejected')
    call(f, 'wrong-pin', attempt('wrong-pin', manifest_sha256='0' * 64), error='rma-stage-manifest-digest-mismatch')
    if args.include_file_content_history:
        call(f, 'v11-without-opt-in', attempt('v11-without-opt-in', include_file_content_history=False),
             error='rma-stage-bundle-member-rejected')
    call(f, 'unsupported-v10', attempt('unsupported-v10', bundle=old_bundle, manifest_sha256=old_pin,
         file_content_root=old_assets, include_file_content_history=False), error='rma-stage-erp-candidate-shape-rejected')
    inject(f, attempt('allocated-token-conflict'), f'ALTER SEQUENCE {importer.SEQUENCE} START WITH 1;',
           f'UPDATE {importer.temporary("Aggregate")} SET "RowVersion"=decode({Q(b_request["RowVersion"][2:])},\'hex\');\n',
           'rma-stage-erp-candidate-token-rejected')
    inject_unique_collision(f, attempt('late-global-collision'), a, b_message)
    call(f, 'after-rollback-recover', attempt('after-rollback-recover', action='recover'),
         error='rma-stage-candidate-receipt-not-found')
    require(boundary(f, 'boundary-after-rollback') == baseline and f.snapshot_erp('a-after-rollback', a) == empty
            and f.snapshot_erp('b-after-rollback', b) == b_expected, 'late native rollback changed durable target')

    call(f, 'import-discarded', attempt('import-discarded'), discard=True)
    return verify_committed(f, args, f.output/'boundary-before.stdout', a_expected, b_expected)


def verify_committed(f, args, before, a_expected, b_expected):
    a, b = f.tenants
    controls = json.loads(before.read_bytes())

    def attempt(label, **kwargs):
        return replace(args, output=f.output / label, **kwargs)

    receipt = call(f, 'recover-original', attempt('recover-original', action='recover'))
    replay = call(f, 'same-key', attempt('same-key'))
    # Hex separates psql's platform-specific row terminator from the stored receipt bytes.
    encoded = f.sql('persisted-receipt-hex',
                    f"SELECT encode(convert_to(result_json,'UTF8'),'hex') FROM {importer.RECEIPTS}.receipt;")
    actual = bytes.fromhex(encoded.decode('ascii'))
    require(receipt == replay == actual, 'original durable receipt bytes differ')
    value = json.loads(receipt)
    source_format = archive.FORMAT_WITH_FILE_CONTENT_HISTORY if args.include_file_content_history else archive.FORMAT_WITH_STOCK_OUTBOX
    require(value['profile'] == importer.PROFILE and value['outcome'] == 'IMPORTED' and value['recoveryReady'] is False
            and value['format'] == source_format
            and value['binding']['target'] == f.target.mapping() and value['binding']['candidateId'] == args.candidate_id
            and value['binding']['tenantId'] == a and value['binding']['manifestSha256'] == args.manifest_sha256
            and value['counts'] == importer.COUNTS and value['rowVersionNext'] == 11, 'durable receipt binding differs')
    call(f, 'recover-wrong-key', attempt('recover-wrong-key', action='recover', manifest_sha256='0' * 64),
         error='rma-stage-candidate-receipt-key-rejected')
    complete = json.loads(boundary(f, 'boundary-after'))
    require(complete == dict(controls, sequence=[11, False], receipt=True), 'triggers, ACL or sequence do not match')
    require(f.snapshot_erp('a-restored', a) == a_expected and f.snapshot_erp('b-retained', b) == b_expected,
            'native bytes, scope or original tokens changed')
    actual_next = child(f.output, 'next-original-token', f.psql, archive.client_environment(f.env, 'w422-next-token'),
                        statement=f'SELECT nextval({Q(importer.SEQUENCE)}::regclass);')
    require(actual_next.strip() == b'11', 'real generator did not advance beyond both tenants')
    return dict(target=f.target.mapping(), pin=args.manifest_sha256, receipt=archive.file_digest(f.output/'recover-original.stdout'),
                boundaryBefore=archive.file_digest(before), nextValue=11, recoveryReady=False)


def parser():
    cli = argparse.ArgumentParser(description=__doc__)
    for name in ('pg-bin','target-file','source-file','bundle','file-content-root','output','b-original','old-bundle','old-assets'):
        cli.add_argument('--' + name, type=Path, required=True)
    for name in ('username','candidate-id','manifest-sha256','old-pin'):
        cli.add_argument('--' + name, required=True)
    for name in ('tenant-a', 'tenant-b'):
        cli.add_argument('--' + name, type=archive.guid, required=True)
    cli.add_argument('--include-file-content-history', action='store_true')
    return cli


def verify(options, committed_from=None):
    archive.private_directory(options.output)
    physical = archive.PhysicalTarget.from_mapping(json.loads(options.target_file.read_bytes()))
    f = Fixture(options, physical, options.output)
    args = Inputs(options.pg_bin, options.target_file, options.username, str(options.tenant_a), options.candidate_id,
                  options.manifest_sha256, options.bundle, options.file_content_root, options.output/'import',
                  include_file_content_history=getattr(options, 'include_file_content_history', False))
    if committed_from is None:
        result = exercise(f, args, options.old_bundle, options.old_pin, options.old_assets, options.b_original, options.source_file)
    else:
        a, b = f.tenants
        result = verify_committed(f, args, committed_from/'boundary-before.stdout', protected_expected(args.bundle, a, ''),
                                  protected_expected(options.b_original, b, 'native-b-'))
    save(f.output / 'actual.json', result)
    print(json.dumps(result, indent=2), flush=True)
    return result


if __name__ == '__main__':
    verify(parser().parse_args())
