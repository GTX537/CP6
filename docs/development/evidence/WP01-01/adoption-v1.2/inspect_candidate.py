"""Read-only structural candidate inspection, never a business execution."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--root', type=Path, required=True)
parser.add_argument('--archive-root', type=Path, required=True)
args = parser.parse_args()
root = args.root.resolve()
sys.path.insert(0, str(root / 'docs/development/tools'))
import acceptance_basis

path = root / 'docs/development/evidence/WP01-01/adoption-v1.2/manifest.json'
raw = path.read_bytes()
ref = {'path': path.relative_to(root).as_posix(), 'sha256': hashlib.sha256(raw).hexdigest(),
       'bytes': len(raw), 'representation': 'RAW_FILE_BYTES'}
result = acceptance_basis.evaluate(root, {'manifest': ref}, archives=(args.archive_root.resolve(),))
result.update(scope='REAL_CANDIDATE_STRUCTURAL_SOURCE_CHECK_ONLY', business_tests_executed=False,
              manifest=ref, archive_root=str(args.archive_root.resolve()))
print(json.dumps(result, ensure_ascii=False, indent=2))
raise SystemExit(not result['inventory_coverage_valid'] or result['basis_adoption_valid']
                 or any(result['target_runtime_valid'].values()))
