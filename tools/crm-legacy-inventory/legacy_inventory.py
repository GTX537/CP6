#!/usr/bin/env python3
"""Read-only, revision-bound lexical inventory for the C04B legacy exit review."""

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys


TABLE_STEMS = (
    'Account', 'Activity', 'Collaborator', 'Contact', 'ErpLink', 'IntakeConfig',
    'IntakeMember', 'Lead', 'MediaAsset', 'MergeRecord', 'Opportunity',
    'PageRevision', 'PageTranslation', 'PublicForm', 'PublicRoute',
    'PublicSubmission', 'Site', 'SitePage', 'SourceTouch', 'StageHistory',
)
DBSETS = (
    'CrmAccounts', 'CrmActivities', 'CrmCollaborators', 'CrmContacts', 'CrmErpLinks',
    'CrmIntakeConfigs', 'CrmIntakeMembers', 'CrmLeads', 'CrmMediaAssets',
    'CrmMergeRecords', 'CrmOpportunities', 'CrmPageRevisions', 'CrmPageTranslations',
    'CrmPublicForms', 'CrmPublicRoutes', 'CrmPublicSubmissions', 'CrmSites',
    'CrmSitePages', 'CrmSourceTouches', 'CrmStageHistories',
)
SUPPORT_SYMBOLS = (
    'CrmLeadStatus', 'CrmOpportunityStage', 'CrmActivityType', 'CrmSourceChannel',
    'CrmPublicSubmissionStatus', 'CrmSiteStatus', 'CrmPageType',
    'CrmPublicationStatus', 'CrmEntityTypes', 'CrmErpEntityTypes', 'CrmStateMachine',
    'CP6.Entity.DomainModels.Crm', 'CP6.Core.Services.Crm',
)
SYMBOLS = sorted(set(['Crm_'+x for x in TABLE_STEMS] +
                     ['Crm'+x for x in TABLE_STEMS] + list(DBSETS) + list(SUPPORT_SYMBOLS)))
TOKEN = re.compile(r'(?<![\w])(?:'+ '|'.join(re.escape(s) for s in sorted(SYMBOLS, key=len, reverse=True)) + r')(?![\w])')
EXTENSIONS = {'.cs', '.csproj', '.props', '.targets', '.sql', '.ps1', '.psm1',
              '.py', '.ts', '.tsx', '.js', '.jsx', '.vue', '.razor', '.cshtml',
              '.json', '.yml', '.yaml', '.xml', '.config', '.sh'}
EXCLUDED_PARTS = {'bin', 'obj', 'node_modules', '.git', '.venv', 'vendor'}
REQUIRED_PROJECTS = {'CP6.Core/CP6.Core.csproj', 'CP6.Entity/CP6.Entity.csproj',
                     'CP6.WebApi/CP6.WebApi.csproj'}
BLOCKING_CATEGORIES = {'runtime', 'entity-model', 'model-snapshot', 'unclassified'}
MAX_BLOB_BYTES = 16 * 1024 * 1024


class InventoryError(RuntimeError):
    """Fixed diagnostic codes only: never echo a source line or caller argument."""


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def git(root, args, input_bytes=None):
    try:
        result = subprocess.run(['git', '-C', str(root)] + args, input=input_bytes,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                timeout=90, check=False)
    except (OSError, subprocess.TimeoutExpired):
        raise InventoryError('C04B_GIT_UNAVAILABLE') from None
    if result.returncode:
        raise InventoryError('C04B_GIT_READ_FAILED')
    return result.stdout


def category(path):
    p = PurePosixPath(path)
    parts = p.parts
    lower = [x.lower() for x in parts]
    if (p.name.startswith('test_') or any(x in ('tests', 'testdata') or x.endswith('tests')
                                       or x.endswith('-tests') or x.endswith('-fixture')
                                       for x in lower)):
        return 'tests'
    if p.name.endswith('ModelSnapshot.cs'):
        return 'model-snapshot'
    if 'Migrations' in parts and p.suffix == '.cs':
        return 'migration-history'
    if path.startswith('CP6.Entity/DomainModels/Crm/'):
        return 'entity-model'
    if parts[0].startswith('CP6.') or parts[0] == 'cp6.web':
        return 'runtime'
    if parts[0] in ('tools', 'eng', 'scripts', 'deploy', '.github', 'contracts', 'migration'):
        return 'operations'
    return 'unclassified'


def selected(path):
    p = PurePosixPath(path)
    return (p.parts[0] != 'docs' and not any(x in EXCLUDED_PARTS for x in p.parts)
            and p.suffix.lower() in EXTENSIONS)


def read_tree(root, revision):
    if not re.fullmatch(r'(?:[0-9a-f]{40}|[0-9a-f]{64})', revision):
        raise InventoryError('C04B_FULL_COMMIT_REQUIRED')
    resolved = git(root, ['rev-parse', '--verify', '--end-of-options', revision+'^{commit}']).decode('ascii').strip()
    if resolved != revision:
        raise InventoryError('C04B_COMMIT_ID_MISMATCH')
    entries = []
    paths = set()
    for row in git(root, ['ls-tree', '-rz', '--full-tree', revision]).split(b'\0'):
        if not row:
            continue
        try:
            metadata, raw_path = row.split(b'\t', 1)
            mode, kind, oid = metadata.decode('ascii').split(' ')
            path = raw_path.decode('utf-8')
        except (ValueError, UnicodeError):
            raise InventoryError('C04B_INVALID_TREE_ENTRY') from None
        if kind != 'blob' or mode not in ('100644', '100755'):
            raise InventoryError('C04B_LINKED_SOURCE_REQUIRES_REVIEW')
        if path in paths:
            raise InventoryError('C04B_DUPLICATE_TREE_PATH')
        paths.add(path)
        if selected(path):
            entries.append({'path': path, 'gitBlob': oid})
    if not REQUIRED_PROJECTS.issubset(paths):
        raise InventoryError('C04B_REPOSITORY_SCOPE_INCOMPLETE')
    return sorted(entries, key=lambda x: x['path']), len(paths)


def read_blobs(root, entries):
    """Read bounded batches; never open a worktree file from a tree-supplied path."""
    for start in range(0, len(entries), 32):
        batch = entries[start:start+32]
        oids = ''.join(x['gitBlob']+'\n' for x in batch).encode('ascii')
        sizes = git(root, ['cat-file', '--batch-check'], oids).splitlines()
        if len(sizes) != len(batch):
            raise InventoryError('C04B_INCOMPLETE_BLOB_BATCH')
        for entry, raw_header in zip(batch, sizes):
            try:
                oid, kind, size = raw_header.decode('ascii').split(' ')
                if oid != entry['gitBlob'] or kind != 'blob' or not 0 <= int(size) <= MAX_BLOB_BYTES:
                    raise ValueError()
            except (UnicodeError, ValueError):
                raise InventoryError('C04B_INVALID_OR_OVERSIZE_BLOB') from None
        raw = git(root, ['cat-file', '--batch'], oids)
        offset = 0
        for entry, expected_header in zip(batch, sizes):
            end = raw.find(b'\n', offset)
            if end < 0 or raw[offset:end] != expected_header:
                raise InventoryError('C04B_INVALID_BLOB_BATCH')
            size = int(expected_header.rsplit(b' ', 1)[1])
            data = raw[end+1:end+1+size]
            offset = end+size+2
            if len(data) != size or raw[offset-1:offset] != b'\n':
                raise InventoryError('C04B_INCOMPLETE_BLOB_BATCH')
            try:
                text = data.decode('utf-8-sig')
            except UnicodeError:
                raise InventoryError('C04B_SOURCE_ENCODING_REQUIRES_REVIEW') from None
            if '\x00' in text:
                raise InventoryError('C04B_BINARY_SOURCE_REQUIRES_REVIEW')
            yield entry, data, text
        if offset != len(raw):
            raise InventoryError('C04B_TRAILING_BLOB_DATA')


def build_inventory(root, revision):
    entries, total = read_tree(root, revision)
    references = []
    for entry, data, text in read_blobs(root, entries):
        matches = []
        for line_no, line in enumerate(text.splitlines(), 1):
            matches.extend({'line': line_no, 'symbol': symbol}
                           for symbol in sorted({x.group(0) for x in TOKEN.finditer(line)}))
        if matches:
            references.append(dict(entry, sha256=sha256(data), category=category(entry['path']), matches=matches))
    counts = Counter(x['category'] for x in references)
    blocking = sum(count for name, count in counts.items() if name in BLOCKING_CATEGORIES)
    tables = []
    for stem, dbset in zip(TABLE_STEMS, DBSETS):
        names = {'Crm_'+stem, 'Crm'+stem, dbset}
        files = [x['path'] for x in references if any(m['symbol'] in names for m in x['matches'])]
        tables.append({'table': 'Crm_'+stem, 'entity': 'Crm'+stem, 'dbSet': dbset,
                       'referencingFiles': files, 'fileCount': len(files)})
    return {
        'format': 'CP6.C04B.LegacyReferenceInventory.v1', 'sourceCommit': revision,
        'toolSha256': sha256(Path(__file__).read_bytes().replace(b'\r\n', b'\n')),
        'catalogVersion': 1, 'catalogSha256': sha256(json.dumps(SYMBOLS, separators=(',', ':')).encode()),
        'scannedFileSetSha256': sha256(json.dumps(entries, separators=(',', ':'), ensure_ascii=False).encode('utf-8')),
        'summary': {'trackedFiles': total, 'scannedFiles': len(entries),
                    'excludedFiles': total-len(entries), 'matchingFiles': len(references),
                    'blockingFiles': blocking, 'filesByCategory': dict(sorted(counts.items()))},
        'tables': tables, 'references': references,
        'runtimeReferencesRemain': blocking > 0,
        'developmentAcceptanceAssessed': False, 'databaseWriteAbsenceVerified': False,
        'productionCutoverVerified': False, 'releaseCycleObservationVerified': False,
        'adoptionVerified': False, 'changesApplied': False,
        'scope': {'input': 'Committed Git tree only; working copy and untracked files are not read.',
                  'extensions': sorted(EXTENSIONS), 'excludedDirectoryParts': sorted(EXCLUDED_PARTS | {'docs'}),
                  'blockingCategories': sorted(BLOCKING_CATEGORIES),
                  'matching': 'Exact lexical identifiers, including comments/string literals; conservative review inventory.',
                  'limitations': 'Not a compiler call graph or database access audit. Dynamic SQL, reflection, external assemblies, unlisted extensions and formal adoption evidence need independent verification.',
                  'protectedCapabilities': 'C01/C02 identity and C03 ERP identifiers are not legacy table/entity matches merely because they start with Crm.',
                  'retention': 'Historical migrations, tests and source-protection tools are reviewed separately; this report never authorizes deletion.'},
    }


def main(arguments=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', required=True, type=Path)
    parser.add_argument('--revision', required=True, help='Full immutable Git commit SHA')
    parser.add_argument('--output', type=Path, help='New JSON file; existing paths are never overwritten')
    parser.add_argument('--require-no-runtime-references', action='store_true')
    args = parser.parse_args(arguments)
    try:
        if args.output is not None and args.output.exists():
            raise InventoryError('C04B_OUTPUT_ALREADY_EXISTS')
        result = build_inventory(args.root, args.revision)
        encoded = (json.dumps(result, ensure_ascii=False, indent=2)+'\n').encode('utf-8')
        if args.output is None:
            sys.stdout.write(encoded.decode('utf-8'))
        else:
            with args.output.open('xb') as stream:
                stream.write(encoded)
        return 2 if args.require_no_runtime_references and result['runtimeReferencesRemain'] else 0
    except InventoryError as error:
        print(str(error), file=sys.stderr)
        return 1
    except (OSError, UnicodeError):
        print('C04B_LOCAL_IO_FAILED', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
