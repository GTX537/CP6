"""Build honest progress navigation from real modules and their author-maintained ledgers."""
import argparse
import json
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--handbook', required=True, type=Path)
p.add_argument('--matrix', required=True, type=Path)
p.add_argument('--delivery', required=True, type=Path)
p.add_argument('--available-content-complete', action='store_true', help='Finalize located current design material; preserves the explicitly missing DEL review')
a = p.parse_args()
matrix = json.loads(a.matrix.read_text(encoding='utf-8'))['targets']
progress = {}
for path in sorted((a.handbook / 'worklogs').glob('*-progress.json'), key=lambda x: (x.name.startswith('root-'), x.name)):
    d = json.loads(path.read_text(encoding='utf-8'))
    targets = d.get('targets', [])
    if isinstance(targets, dict):
        targets = [dict(r, target_id=k) for k, r in targets.items()]
    for r in targets:
        if str(r.get('status', '')).startswith('transferred_to_'):
            continue
        progress[r['target_id']] = r
labels = {'core_semantics_consolidated': '核心规则已整理，必要附件/集中核对尚待闭合', 'required_materials_consolidated': '必要材料已整理（不代表实现/运行通过）', 'draft_with_gaps': '详细初稿，有明确未读范围', 'reading': '正在补读', 'pending': '待整理', 'queued': '待整理', 'not_started': '待整理'}
records = []
for target in matrix:
    tid = target['target_id']; state = progress.get(tid, {})
    file = a.handbook / 'modules' / (tid + '.md')
    status = state.get('document_status') or state.get('status') or state.get('state', 'pending')
    basis = target['design_basis']
    version = basis.get('effective_version_and_precedence') or basis.get('version_and_combination') or basis.get('selected_version') or basis.get('version_label')
    assert version, (tid, list(basis))
    records.append({'target_id': tid, 'name': target['name'], 'version': version, 'module_written': file.is_file(), 'document_status': status, 'next_action': state.get('next_action'), 'module_path': (a.delivery / 'modules' / file.name).as_posix() if file.is_file() else None})
counts = Counter(r['document_status'] for r in records)
stamp = datetime.now(timezone.utc).isoformat()
if a.available_content_complete:
    unresolved = [r['target_id'] for r in records if r['document_status'] != 'required_materials_consolidated']
    assert len(records) == 125 and all(r['module_written'] for r in records)
    assert unresolved == ['WMS-DEL-01'], unresolved
    assert progress['WMS-DEL-01']['required_input_closure']['all_located_current_necessary_semantics'] is True
    assert progress['WMS-DEL-01']['required_input_closure']['missing_component'] == 'R01071'
summary = {'as_of_utc': stamp, 'target_count': len(records), 'written_modules': sum(r['module_written'] for r in records), 'status_counts': dict(counts), 'available_current_materials_consolidated': a.available_content_complete, 'all_required_materials_present': False, 'interpretation': 'Located current design consolidation, missing original evidence, implementation and runtime acceptance are separate states.'}
(a.handbook / 'indexes/module-navigation.json').write_text(json.dumps({'summary': summary, 'targets': records}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
if a.available_content_complete:
    required_path = a.handbook / 'indexes/required-inputs.json'
    required = json.loads(required_path.read_text(encoding='utf-8'))
    states = {r['target_id']: r['document_status'] for r in records}
    for target in required['targets']:
        target['semantic_consolidation'] = states[target['target_id']]
        if target['target_id'] == 'WMS-DEL-01':
            target['semantic_consolidation_note'] = 'All located required content consolidated; exact R01071 review original remains missing.'
    required_path.write_text(json.dumps(required, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
scope_text = '已定位的当前必要内容已整理；WMS-DEL独审R01071仍缺原件。' if a.available_content_complete else '其余继续整理。'
lines = ['# 125目标模块目录', '', f'快照时间：{stamp}。已形成{summary["written_modules"]}份模块正文。{scope_text}状态由实际作者记录，不将有文件视为必要附件已全部核完。', '', '每个版本说明继承固定输入矩阵；准确SHA/组合见required-inputs。版本文字不取代接受限定。', '', '| 目标 | 名称与文档 | 当前选定组合 | 整理状态 |', '|---|---|---|---|']
for r in records:
    name = f'[{r["name"]}]({r["module_path"]})' if r['module_written'] else r['name']
    status = labels.get(r['document_status'], r['document_status'])
    if a.available_content_complete and r['target_id'] == 'WMS-DEL-01':
        status = '已定位必要内容已整理；指定独审原件R01071仍缺失'
    if not r['module_written']:
        status += '；尚未生成正文'
    lines.append('| ' + ' | '.join(x.replace('|', '\\|').replace('\n', ' ') for x in (r['target_id'], name, r['version'], status)) + ' |')
(a.handbook / '模块目录.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
root = a.delivery.as_posix()
opening = ('**状态：现有资料的本地设计整理已完成，缺失原件明确保留。** 125份模块正文已形成，124个目标必要材料已整理，WMS-DEL-01现有必要内容已整理但仍缺指定独审R01071；不声明全部原件齐备或运行通过。' if a.available_content_complete else '**状态：持续整理中，尚未完成全部125个目标及其必要附件。**')
scope_status = '现有必要内容已整理；历史、元数据、候选源码/工具、视觉母版及未取得原件按原阅读账区分，不能据此声称所有历史源码/示例均已全文审查。' if a.available_content_complete else '当前仍有未读必要材料，本任务保持进行中。'
readme = f'''# CP6开发设计文档

{opening} 本地快照更新于{stamp}；具体范围和后续事项在模块末节与阅读记录中保留。

从[开发者与AI阅读指南]({root}/开发者与AI阅读指南.md)开始，再按[125目标模块目录]({root}/模块目录.md)进入业务模块。

本地整理范围、验证与后续事项见[整理交付报告]({root}/整理交付报告.md)。

## 公共设计

- [系统边界与开发地图]({root}/architecture/01-系统边界与开发地图.md)：业务职责、端到端阅读链、现有代码起点。
- [事务、回执与恢复]({root}/architecture/02-事务回执与恢复.md)：真正共同事务、最终授权、UNKNOWN、原结果及跨Owner应用。
- [资料版本与接受边界]({root}/architecture/03-资料版本与接受边界.md)：选择器、准确SHA组合、继承设计、勘误与验收状态。
- [关键身份与状态词典]({root}/architecture/04-关键身份与状态词典.md)：业务意图、运输键、版本、库存份额、回执和运行状态。
- [资料缺口与集成待核]({root}/evidence/资料缺口与集成待核.md)：21份精确原件、3个受阻包及明确跨域合同差异。

## 文件与来源范围

本次只读盘点指定归档下14,540个物理文件（21,500,364,243字节），14,387组不同SHA。所有物理文件都有用途与保留记录；342个不同ZIP建立成员索引，分片/gzip准确还原记录另列。23个Excel与3个Word完成只读结构抽取，未把提取或哈希检查称为语义全文审读。

当前设计范围125个Target，必需引用2,181次。L1/L2材料可定位124/125，全部声明层级可定位121/125；这是资料状态，与本手册整理进度和运行状态分别计算。{scope_status}

| 要查什么 | 入口 |
|---|---|
| 全部物理文件与整理去向 | [CSV]({root}/indexes/all-file-dispositions.csv) / [完整JSON]({root}/indexes/all-file-dispositions.json) |
| 逐目标有效输入与原件SHA | [required-inputs.json]({root}/indexes/required-inputs.json) |
| 规范章节、代码块与原文行段 | [结构索引]({root}/indexes/required-source-structure.json) |
| ZIP内部原始成员 | [容器索引]({root}/indexes/container-members.json) |
| 精确SHA到本地文件 | [真实路径映射]({root}/indexes/materialized-content-paths.json) |
| 最新增补319固定身份 | [来源登记]({root}/indexes/supplement-source-register.csv) |
| 仍需找回的准确原件 | [缺件清单]({root}/evidence/missing-source-register.csv) |
| 商业 / 执行 / 公共平台合同 | [商业]({root}/contracts/commercial-contracts.json) / [执行]({root}/contracts/operations-contracts.json) / [平台工程]({root}/contracts/platform-engineering-contracts.json) |

原件保存在`D:/CP6/docs/CP6_完整成果归档_20261009`，展开阅读对象保存在`D:/CP6-archives/consolidation-20261010`。本目录是本地私有设计整理，原归档与上一轮整合报告保持原身份；没有业务编码、业务测试、Actions、数据库迁移、发布或部署。

整理目标、验收与实际完成条件见[整理计划]({root}/整理计划.md)。源文档中的历史工作指令不构成当前授权。开发任务仍遵守D:/CP6的AGENTS.md规则。
'''
(a.handbook / 'README.md').write_text(readme, encoding='utf-8')
print(json.dumps(summary, ensure_ascii=False))
