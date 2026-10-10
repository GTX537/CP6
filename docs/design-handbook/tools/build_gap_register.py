"""Recheck exact historical gaps against the newly expanded byte identity map."""
import argparse
import csv
import hashlib
import json
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--previous', required=True, type=Path)
p.add_argument('--indexes', required=True, type=Path)
p.add_argument('--evidence', required=True, type=Path)
a = p.parse_args()
known = json.loads((a.indexes / 'materialized-content-paths.json').read_text(encoding='utf-8'))
supp = json.loads((a.indexes / 'supplement-source-register.json').read_text(encoding='utf-8'))
rows = list(csv.DictReader(a.previous.open(encoding='utf-8-sig', newline='')))
out = []
for row in rows:
    sha = row['期望SHA256']
    path = known.get(sha)
    if path:
        data = Path(path).read_bytes()
        assert hashlib.sha256(data).hexdigest() == sha
        assert len(data) == int(row['期望字节数'])
    out.append({**row, '本轮本地复核': 'exact_bytes_found_pending_semantic_read' if path else 'unlocated_after_full_physical_container_and_representation_index', '本地路径': path})
blocked = [r for r in supp['records'] if r['status'] == 'blocked_unread_official_materialization']
for r in blocked:
    if any(r['name'] == row['原文件名或准确描述'] for row in out):
        continue
    out.append({'编号': 'M28', '优先级': '增补历史包访问受阻', '所属目标': 'ERP-CREDIT-01（来源登记，正文未取得）', '原文件名或准确描述': r['name'], '期望字节数': r['size_bytes'], '期望SHA256': r.get('actual_sha256') or '来源未提供，取得后核原身份', '记录来源': r['library_file_id'] + ' / ' + r['file_id'], '来源字段': supp['status_snapshot_path'], '状态': r['status'], '找到后核对': '准确原ZIP身份及范围；不以新生成替代包当历史原件', '本轮本地复核': 'unlocated_source_reported_blocked', '本地路径': None})
a.evidence.mkdir(parents=True, exist_ok=True)
result = {'scope': 'Exact prior missing identities rechecked against local physical files, indexed ZIP members, restored byte/gzip representations. No new network retrieval or slice search is claimed.', 'previous_register': a.previous.as_posix(), 'source_status_as_of_utc': supp['status_as_of_utc'], 'counts': {'exact_original_missing': sum(r['编号'] not in ('M26', 'M27', 'M28') and not r['本地路径'] for r in out), 'blocked_containers': sum(r['编号'] in ('M26', 'M27', 'M28') for r in out), 'newly_found_exact_originals': sum(bool(r['本地路径']) for r in out)}, 'records': out}
(a.evidence / 'missing-source-register.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
fields = list(rows[0]) + ['本轮本地复核', '本地路径']
with (a.evidence / 'missing-source-register.csv').open('w', encoding='utf-8-sig', newline='') as f:
    w = csv.DictWriter(f, fieldnames=fields); w.writeheader(); w.writerows(out)
print(json.dumps(result['counts']))
