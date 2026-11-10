#!/usr/bin/env python3
"""Check v11 verification with a synthetic FileContent graph and pinned old bundles."""
import argparse
import csv
from hashlib import sha256
import json
from pathlib import Path
import shutil
import subprocess
import sys
from unittest.mock import patch
from uuid import UUID

sys.dont_write_bytecode = True
import archive
import file_content_assets as assets
import file_content_history as history


FORMAT = 'cp6-rma-source-archive-v11'
CLI = Path(archive.__file__).resolve()
ACTOR = '10101010-1010-4010-8010-101010101010'
FILE = '11111111-1111-4111-8111-111111111111'
PARENT = '22222222-2222-4222-8222-222222222222'
STORE = '33333333333343338333333333333333'
ETAGS = ('44444444-4444-4444-8444-444444444444', '55555555-5555-4555-8555-555555555555')
OPERATIONS = ('66666666-6666-4666-8666-666666666666', '77777777-7777-4777-8777-777777777777',
              '88888888-8888-4888-8888-888888888888')
AT = '2026-11-09 20:00:00+00'


def require(value, message):
    if not value:
        raise ValueError(message)


def manifest(bundle):
    return json.loads((bundle/'manifest.json').read_bytes())


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def pg_csv(path, columns, rows):
    with path.open('w', encoding='utf-8', newline='') as stream:
        output = csv.writer(stream, lineterminator='\n')
        output.writerow(columns)
        output.writerows(rows)


def json_bytes(value):
    data = json.dumps(value, separators=(',', ':')).encode('utf-8')
    return '\\x' + data.hex(), sha256(data).hexdigest()


def synthetic(v10, output):
    bundle = output/'synthetic-v11'
    shutil.copytree(v10, bundle)
    combined = manifest(bundle)
    tenant = combined['tenantId']
    root = output/'synthetic-assets'
    key = 'native-content-v1/' + UUID(tenant).hex + '/' + STORE
    path = root/key
    path.parent.mkdir(parents=True)
    content = b'W429 synthetic verifier fixture only\n'
    path.write_bytes(content)
    content_sha = sha256(content).hexdigest()
    parent_body, parent_sha = json_bytes(dict(tenantId=tenant,id=PARENT,version='1'))
    common = (PARENT,1,parent_sha,'report, synthetic.txt',content_sha,len(content),'text/plain',key)
    histories = {
        'contents': [(tenant,FILE,ACTOR,*common)],
        'heads': [(tenant,FILE,2)],
        'events': [],
        'operations': [],
        'parent-versions': [(tenant,PARENT,1,parent_body,parent_sha,ACTOR,AT)],
    }
    for revision, (etag, operation, action) in enumerate(zip(ETAGS, OPERATIONS, ('UPLOAD','HOLD')), 1):
        event, state_digest = json_bytes(dict(tenantId=tenant,id=FILE,version=str(revision),actorId=ACTOR,
            etag=etag,action=action,held=revision == 2,inspection=dict(status='PENDING')))
        histories['events'].append((tenant,FILE,revision,event,state_digest,ACTOR,AT))
        input_bytes, input_sha = json_bytes(dict(fileId=FILE,version=str(revision)))
        receipt, _ = json_bytes(dict(OperationId=operation,Kind='FILE_CONTENT',ObjectId=FILE,
                                     Version=str(revision),Digest=state_digest))
        histories['operations'].append((tenant,assets.OWNER,operation,ACTOR,history.CONTRACT,'COMMITTED',
                                        input_bytes,input_sha,receipt,'{' + FILE + '}','{}',AT))
    closed, _ = json_bytes(dict(OperationId=OPERATIONS[2],State='CLOSED',Result=None))
    histories['operations'].append((tenant,assets.OWNER,OPERATIONS[2],ACTOR,history.CONTRACT,'CLOSED',
                                    None,None,closed,'{}','{}',AT))
    members = {}
    for key, part in history.MEMBERS.items():
        name = history.filename(key)
        pg_csv(bundle/name, part.columns, histories[key])
        members[name] = {**history.member_shape(part), 'rows':len(histories[key])}
        combined['members'][name] = archive.file_digest(bundle/name)
    pg_csv(bundle/assets.MEMBER, assets.COLUMNS, [(tenant,FILE,*common,2,ETAGS[1],state_digest)])
    combined['members'][assets.MEMBER] = archive.file_digest(bundle/assets.MEMBER)
    combined['fileContentAssets'] = assets.manifest_section(1)
    combined['fileContentHistory'] = dict(format=history.FORMAT, dependencies=history.DEPENDENCIES,
                                          assetProjection=assets.MEMBER, members=members)
    combined['format'] = FORMAT
    save(bundle/'manifest.json', combined)
    return bundle, root


def invoke(output, label, command, expected=None):
    result = subprocess.run(list(map(str, [sys.executable,'-B',*command])),capture_output=True,
                            creationflags=archive.FLAGS,timeout=30)
    (output/(label+'.stdout')).write_bytes(result.stdout)
    (output/(label+'.stderr')).write_bytes(result.stderr)
    (output/(label+'.exit')).write_text(str(result.returncode)+'\n',encoding='ascii')
    if expected:
        require(result.returncode == 1 and expected.encode('ascii') in result.stderr and not result.stdout,
                label + ' rejected with a different reason')
        return None
    require(result.returncode == 0 and not result.stderr, label + ' failed: ' + result.stderr.decode(errors='replace').strip())
    return json.loads(result.stdout)


def verify(output, label, bundle, root=None, *, expected=None, tenant=None):
    command = [CLI,'verify','--bundle',bundle,'--tenant',tenant or manifest(bundle)['tenantId']]
    if root is not None:
        command += ['--file-content-root',root]
    return invoke(output,label,command,expected)


def variant(output, bundle, label):
    path = output/label
    shutil.copytree(bundle,path)
    return path,manifest(path)


def replace_cell(bundle, metadata, name, column, value):
    path = bundle/name
    with path.open(encoding='utf-8',newline='') as stream:
        columns,*rows = csv.reader(stream,strict=True)
    rows[0][columns.index(column)] = value
    pg_csv(path,columns,rows)
    metadata['members'][name] = archive.file_digest(path)


def rejected_members(output, bundle, root):
    event = history.filename('events')
    contents = history.filename('contents')
    parent = history.filename('parent-versions')
    rejected = 'file-content-history-archive-'

    wrong, metadata = variant(output,bundle,'wrong-type')
    metadata['fileContentHistory']['members'][event]['types'][1] = 'text'
    save(wrong/'manifest.json',metadata)
    verify(output,'wrong-type',wrong,root,expected=rejected+'section-rejected')

    wrong, metadata = variant(output,bundle,'wrong-snapshot-type')
    metadata['snapshot']['serverVersionNum'] = True
    save(wrong/'manifest.json',metadata)
    verify(output,'wrong-snapshot-type',wrong,root,expected=rejected+'source-metadata-rejected')

    wrong, _ = variant(output,bundle,'missing-member')
    (wrong/event).unlink()
    verify(output,'missing-member',wrong,root,expected='bundle-member-mismatch')

    wrong, metadata = variant(output,bundle,'wrong-count')
    metadata['fileContentHistory']['members'][event]['rows'] += 1
    save(wrong/'manifest.json',metadata)
    verify(output,'wrong-count',wrong,root,expected=rejected+'count-mismatch')

    wrong, metadata = variant(output,bundle,'foreign-history-row')
    replace_cell(wrong,metadata,parent,'tenant_id',PARENT)
    save(wrong/'manifest.json',metadata)
    verify(output,'foreign-history-row',wrong,root,expected=rejected+'row-scope-rejected')

    wrong, metadata = variant(output,bundle,'invalid-native-bytea')
    replace_cell(wrong,metadata,event,'body_bytes','\\xZZ')
    save(wrong/'manifest.json',metadata)
    verify(output,'invalid-native-bytea',wrong,root,expected=rejected+'row-type-rejected')

    wrong, metadata = variant(output,bundle,'different-store-path')
    different_key = 'native-content-v1/' + UUID(metadata['tenantId']).hex + '/' + UUID(FILE).hex
    replace_cell(wrong,metadata,contents,'store_path',different_key)
    save(wrong/'manifest.json',metadata)
    verify(output,'different-store-path',wrong,root,expected=rejected+'projection-rejected')

    wrong, metadata = variant(output,bundle,'invalid-tenant-store-path')
    foreign_key = 'native-content-v1/' + UUID(PARENT).hex + '/' + STORE
    for name in (contents,assets.MEMBER):
        replace_cell(wrong,metadata,name,'store_path',foreign_key)
    save(wrong/'manifest.json',metadata)
    verify(output,'invalid-tenant-store-path',wrong,root,expected='file-content-asset-key-rejected')

    wrong, metadata = variant(output,bundle,'missing-parent-key')
    for name in (contents,assets.MEMBER):
        replace_cell(wrong,metadata,name,'parent_id',ACTOR)
    save(wrong/'manifest.json',metadata)
    verify(output,'missing-parent-key',wrong,root,expected=rejected+'parent-rejected')

    wrong, metadata = variant(output,bundle,'wrong-current-state')
    replace_cell(wrong,metadata,assets.MEMBER,'state_etag',ETAGS[0])
    save(wrong/'manifest.json',metadata)
    verify(output,'wrong-current-state',wrong,root,expected=rejected+'projection-rejected')

    wrong, metadata = variant(output,bundle,'wrong-original-actor')
    replace_cell(wrong,metadata,history.filename('operations'),'actor_id',PARENT)
    save(wrong/'manifest.json',metadata)
    verify(output,'wrong-original-actor',wrong,root,expected=rejected+'operation-rejected')

    wrong_root = output/'same-length-wrong-bytes'
    shutil.copytree(root,wrong_root)
    path, = wrong_root.glob('native-content-v1/*/*')
    raw = path.read_bytes()
    path.write_bytes(bytes([raw[0] ^ 1]) + raw[1:])
    verify(output,'wrong-asset-bytes',bundle,wrong_root,expected='file-content-asset-digest-mismatch')


def prerequisites(output, bundle, root):
    original = manifest(bundle)
    target = output/'offline-target.json'
    save(target,original['source'])
    pg_bin = output/'no-native-binaries'
    pg_bin.mkdir()
    flags = (
        ('--include-original-journal','rma-source-journal-required'),
        ('--include-rma-source','rma-visibility-source-required'),
        ('--include-rma-visibility','identity-priority-visibility-required'),
        ('--include-identity-priority','rma-iam-reference-identity-priority-required'),
        ('--include-rma-iam-references','file-content-iam-references-required'),
        ('--file-content-root','erp-integration-file-content-required'),
        ('--include-erp-integration','privacy-evidence-needs-erp-integration'),
        ('--include-privacy-evidence','stock-outbox-needs-privacy-evidence'),
        ('--include-main-stock-outbox','file-content-history-needs-main-stock-outbox'),
    )
    command = [CLI,'export','--pg-bin',pg_bin,'--target-file',target,'--username','unused-offline',
               '--installation',original['installationId'],'--tenant',original['tenantId'],'--include-file-content-history']
    for omitted, error in flags:
        selected = [flag for flag,_ in flags if flag != omitted]
        if '--file-content-root' in selected:
            selected.insert(selected.index('--file-content-root')+1,root)
        name = 'without-' + omitted[2:]
        target_output = output/(name+'-unwanted')
        invoke(output,name,[*command,*selected,'--output',target_output],error)
        require(not target_output.exists(),name+' created output before prerequisites passed')

    selected = [flag for flag,_ in flags]
    selected.insert(selected.index('--file-content-root')+1,root)
    unwanted = root/'unwanted-export'
    invoke(output,'output-overlaps-assets',[*command,*selected,'--output',unwanted],
           'file-content-history-output-overlaps-assets')
    require(not unwanted.exists(),'archive output appeared in asset root')

    stage_output = output/'stage-rejected-output'
    invoke(output,'existing-stage-rejects-v11',[CLI.with_name('stage.py'),'stage','--pg-bin',pg_bin,
        '--target-file',target,'--username','unused-offline','--tenant',original['tenantId'],
        '--manifest-sha256',archive.file_digest(bundle/'manifest.json')['sha256'],'--bundle',bundle,
        '--file-content-root',root,'--output',stage_output],'rma-stage-bundle-member-rejected')
    require(not (stage_output/'driver.sql').exists(),'existing stage built a v11 driver')


def execute(args):
    archive.private_directory(args.output)
    require(verify(args.output,'original-v1',args.v1)['format'] == 'cp6-rma-source-archive-v1', 'wrong old v1')
    require(verify(args.output,'original-v10',args.v10,args.v10_assets)['format'] == 'cp6-rma-source-archive-v10', 'wrong old v10')
    old = manifest(args.standalone)
    verified = invoke(args.output,'standalone-history',[Path(history.__file__),'verify','--bundle',args.standalone,
        '--tenant',old['tenantId'],'--manifest-sha256',archive.file_digest(args.standalone/'manifest.json')['sha256'],
        '--file-content-root',args.standalone_assets])
    require(verified['format'] == history.FORMAT and verified['recoveryReady'] is False, 'wrong standalone history')
    bundle, root = synthetic(args.v10,args.output)
    result = verify(args.output,'synthetic-v11',bundle,root)
    require(result['format'] == FORMAT and result['recoveryReady'] is False, 'wrong combined result')
    with patch.object(assets,'verify_file',wraps=assets.verify_file) as asset_read:
        archive.verify_bundle(bundle,manifest(bundle)['tenantId'],file_content_root=root)
    require(asset_read.call_count == 1,'the same external file was hashed more than once')
    rejected_members(args.output,bundle,root)
    prerequisites(args.output,bundle,root)
    print('Original v1, v10 and standalone history verified. Synthetic v11, boundaries and one external read verified offline.',flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('v1','v10','v10-assets','standalone','standalone-assets','output'):
        parser.add_argument('--' + name, required=True, type=Path)
    execute(parser.parse_args())
